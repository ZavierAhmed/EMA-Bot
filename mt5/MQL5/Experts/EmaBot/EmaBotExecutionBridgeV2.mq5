#property strict
#property version   "2.00"
#property description "EMA-Bot MT5 v2 execution Named Pipe adapter. Demo-only; attach separately from EmaBotBridgeV1. Execution disabled by default."

input string InpPipeName="ema-bot.mt5.bridge.v2";
input string InpHandshakeSecret="";
input bool   InpEnableDemoExecution=false;
input string InpExpectedAccountFingerprint="";
input string InpExpectedServer="";
input long   InpMagicNumber=20260817;
input uint   InpPollMilliseconds=100;
input uint   InpHeartbeatSeconds=5;
input uint   InpReconnectMilliseconds=1000;
input uint   InpMaxFrameBytes=1048576;

#define PROTOCOL_VERSION 2
#define MAX_FRAMES_PER_TIMER 16

enum BridgeState { BRIDGE_DISCONNECTED, BRIDGE_CONNECTING, BRIDGE_AWAITING_HELLO_ACK, BRIDGE_CONNECTED };
enum ReceiveState { RECEIVE_HEADER, RECEIVE_PAYLOAD };
enum PipeDirectionState { PIPE_NONE, PIPE_READING, PIPE_WRITING };

int g_pipe=INVALID_HANDLE;
BridgeState g_state=BRIDGE_DISCONNECTED;
ReceiveState g_receive_state=RECEIVE_HEADER;
PipeDirectionState g_direction=PIPE_NONE;
uchar g_header[];
uchar g_payload[];
uint g_expected_payload=0;
ulong g_next_reconnect=0;
ulong g_next_heartbeat=0;
ulong g_last_connect_log=0;

int OnInit()
{
   if(!IsSafePipeName(InpPipeName) || StringLen(InpHandshakeSecret)<32 || InpPollMilliseconds==0 || InpHeartbeatSeconds==0 || InpReconnectMilliseconds==0 || InpMaxFrameBytes==0)
   {
      Print("EmaBot Bridge input validation failed.");
      return INIT_PARAMETERS_INCORRECT;
   }
   ArrayResize(g_header,4);
   EventSetMillisecondTimer(InpPollMilliseconds);
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   EventKillTimer();
   ClosePipe();
}

void OnTimer()
{
   const ulong now=GetTickCount64();
   if(g_state==BRIDGE_DISCONNECTED && now>=g_next_reconnect)
      ConnectPipe(now);
   if(g_pipe==INVALID_HANDLE)
      return;
   PollFrames();
   if(g_state==BRIDGE_CONNECTED && now>=g_next_heartbeat)
   {
      SendHeartbeat();
      g_next_heartbeat=now+(ulong)InpHeartbeatSeconds*1000;
   }
}

bool IsSafePipeName(const string value)
{
   const int length=StringLen(value);
   if(length==0 || length>128) return false;
   for(int i=0;i<length;i++)
   {
      const ushort c=(ushort)StringGetCharacter(value,i);
      if(!((c>='a' && c<='z') || (c>='A' && c<='Z') || (c>='0' && c<='9') || c=='.' || c=='_' || c=='-')) return false;
   }
   return true;
}

void ConnectPipe(const ulong now)
{
   g_state=BRIDGE_CONNECTING;
   const string path="\\\\.\\pipe\\"+InpPipeName;
   ResetLastError();
   g_pipe=FileOpen(path,FILE_READ|FILE_WRITE|FILE_BIN);
   if(g_pipe==INVALID_HANDLE)
   {
      if(now-g_last_connect_log>=5000)
      {
         Print("EmaBot Bridge waiting for local pipe server.");
         g_last_connect_log=now;
      }
      g_state=BRIDGE_DISCONNECTED;
      g_next_reconnect=now+InpReconnectMilliseconds;
      return;
   }
   g_direction=PIPE_NONE;
   ResetReceiveState();
   if(!SendHello())
   {
      ClosePipe();
      g_next_reconnect=now+InpReconnectMilliseconds;
      return;
   }
   g_state=BRIDGE_AWAITING_HELLO_ACK;
}

void ClosePipe()
{
   if(g_pipe!=INVALID_HANDLE)
      FileClose(g_pipe);
   g_pipe=INVALID_HANDLE;
   g_state=BRIDGE_DISCONNECTED;
   g_direction=PIPE_NONE;
   ResetReceiveState();
   g_next_reconnect=GetTickCount64()+InpReconnectMilliseconds;
}

void ResetReceiveState()
{
   g_receive_state=RECEIVE_HEADER;
   g_expected_payload=0;
   ArrayResize(g_payload,0);
}

bool PrepareRead()
{
   if(g_pipe==INVALID_HANDLE) return false;
   if(g_direction==PIPE_WRITING)
   {
      FileFlush(g_pipe);
      if(!FileSeek(g_pipe,0,SEEK_SET)) return false;
   }
   g_direction=PIPE_READING;
   return true;
}

bool PrepareWrite()
{
   if(g_pipe==INVALID_HANDLE) return false;
   if(g_direction==PIPE_READING)
   {
      FileFlush(g_pipe);
      if(!FileSeek(g_pipe,0,SEEK_SET)) return false;
   }
   g_direction=PIPE_WRITING;
   return true;
}

void PollFrames()
{
   for(int frame=0;frame<MAX_FRAMES_PER_TIMER;frame++)
   {
      if(!PrepareRead()) { ClosePipe(); return; }
   const ulong available=FileSize(g_pipe);
      if(g_receive_state==RECEIVE_HEADER)
      {
         if(available<4) return;
         if(FileReadArray(g_pipe,g_header,0,4)!=4) { ClosePipe(); return; }
         g_expected_payload=(uint)g_header[0] | ((uint)g_header[1]<<8) | ((uint)g_header[2]<<16) | ((uint)g_header[3]<<24);
         if(g_expected_payload==0 || g_expected_payload>InpMaxFrameBytes) { ClosePipe(); return; }
         ArrayResize(g_payload,(int)g_expected_payload);
         g_receive_state=RECEIVE_PAYLOAD;
      }
      if(g_receive_state==RECEIVE_PAYLOAD)
      {
         if(FileSize(g_pipe)<(ulong)g_expected_payload) return;
         if(FileReadArray(g_pipe,g_payload,0,(int)g_expected_payload)!=(int)g_expected_payload) { ClosePipe(); return; }
         const string json=CharArrayToString(g_payload,0,(int)g_expected_payload,CP_UTF8);
         ResetReceiveState();
         ProcessFrame(json);
         if(g_pipe==INVALID_HANDLE) return;
      }
   }
}

void ProcessFrame(const string json)
{
   string kind,operation,request_id,payload;
   int version=0;
   if(!GetTopLevelInt(json,"protocolVersion",version) || !GetTopLevelString(json,"kind",kind) || !GetTopLevelString(json,"operation",operation) || !GetTopLevelRaw(json,"requestId",request_id) || !GetTopLevelRaw(json,"payload",payload))
   {
      ClosePipe();
      return;
   }
   if(version!=PROTOCOL_VERSION) { ClosePipe(); return; }
   if(g_state==BRIDGE_AWAITING_HELLO_ACK)
   {
      if(kind=="HelloAck" && operation=="Hello")
      {
         g_state=BRIDGE_CONNECTED;
         g_next_heartbeat=GetTickCount64()+(ulong)InpHeartbeatSeconds*1000;
         return;
      }
      ClosePipe();
      return;
   }
   if(g_state!=BRIDGE_CONNECTED || kind!="Request" || request_id=="null") return;
   HandleRequest(operation,request_id,payload);
}

void HandleRequest(const string operation,const string request_id,const string payload)
{
   if(operation=="Ping") { SendResponse(operation,request_id,"{\"pong\":true}"); return; }
   if(operation=="GetAccount") { SendAccount(request_id); return; }
   if(operation=="GetInstruments") { SendInstruments(request_id); return; }
   string broker_symbol;
   if((operation=="GetInstrument" || operation=="GetQuote") && GetTopLevelString(payload,"brokerSymbol",broker_symbol))
   {
      if(operation=="GetInstrument") SendInstrument(request_id,broker_symbol);
      else SendQuote(request_id,broker_symbol);
      return;
   }
   if(operation=="GetLatestBars") { SendLatestBars(request_id,payload); return; }
   if(operation=="GetBarsRange") { SendBarsRange(request_id,payload); return; }
   if(operation=="GetBarSnapshot") { SendBarSnapshot(request_id,payload); return; }
   if(operation=="CalculateMargin") { SendCalculatedMargin(request_id,payload); return; }
   if(operation=="CalculateProfit") { SendCalculatedProfit(request_id,payload); return; }
   if(operation=="GetExecutionAccount") { SendExecutionAccount(request_id); return; }
   if(operation=="OrderCheck") { HandleExecutionOrder("OrderCheck",request_id,payload); return; }
   if(operation=="SubmitMarketOrder") { HandleExecutionOrder("SubmitMarketOrder",request_id,payload); return; }
   if(operation=="ClosePosition") { HandleClosePosition(request_id,payload); return; }
   if(operation=="ModifyPositionProtection") { HandleModifyPositionProtection(request_id,payload); return; }
   if(operation=="GetPosition") { SendExecutionPosition(request_id,payload); return; }
   if(operation=="GetExecutionHistory") { SendExecutionHistory(request_id,payload); return; }
   if(operation=="GetExactDeal") { SendExactDeal(request_id,payload); return; }
   if(operation=="GetPositionHistory") { SendPositionHistory(request_id,payload); return; }
   if(operation=="GetInstrument" || operation=="GetQuote") { SendError(operation,request_id,"InvalidRequest","brokerSymbol is required.",false); return; }
   SendError(operation,request_id,"UnsupportedOperation","The bridge operation is not supported.",false);
}

bool TryMapTimeframe(const string canonical,ENUM_TIMEFRAMES &period)
{
   if(canonical=="3m") { period=PERIOD_M3; return true; } if(canonical=="5m") { period=PERIOD_M5; return true; }
   if(canonical=="15m") { period=PERIOD_M15; return true; } if(canonical=="30m") { period=PERIOD_M30; return true; }
   if(canonical=="1h") { period=PERIOD_H1; return true; } if(canonical=="2h") { period=PERIOD_H2; return true; }
   if(canonical=="4h") { period=PERIOD_H4; return true; } if(canonical=="6h") { period=PERIOD_H6; return true; }
   if(canonical=="8h") { period=PERIOD_H8; return true; } if(canonical=="12h") { period=PERIOD_H12; return true; }
   if(canonical=="1d") { period=PERIOD_D1; return true; } if(canonical=="1w") { period=PERIOD_W1; return true; }
   if(canonical=="1M") { period=PERIOD_MN1; return true; } return false;
}

bool TryBarRequest(const string operation,const string request_id,const string payload,string &symbol,string &timeframe,ENUM_TIMEFRAMES &period)
{
   if(!GetTopLevelString(payload,"brokerSymbol",symbol) || !GetTopLevelString(payload,"timeframe",timeframe)) { SendError(operation,request_id,"InvalidRequest","brokerSymbol and timeframe are required.",false); return false; }
   if(!TryMapTimeframe(timeframe,period)) { SendError(operation,request_id,"UnsupportedTimeframe","The requested timeframe is not native to MT5.",false); return false; }
   bool custom=false;
   if(!SymbolExist(symbol,custom) || !((bool)SymbolInfoInteger(symbol,SYMBOL_SELECT))) { SendError(operation,request_id,"NotFound","The requested symbol was not found in Market Watch.",false); return false; }
   return true;
}

string RateJson(const string symbol,const string timeframe,const MqlRates &rate,const bool current)
{
   return "{\"brokerSymbol\":\""+JsonEscape(symbol)+"\",\"timeframe\":\""+JsonEscape(timeframe)+"\",\"openTimeUtc\":\""+IsoUtcSeconds(rate.time)+"\",\"open\":"+Number(rate.open)+",\"high\":"+Number(rate.high)+",\"low\":"+Number(rate.low)+",\"close\":"+Number(rate.close)+",\"tickVolume\":"+(string)rate.tick_volume+",\"realVolume\":"+(string)rate.real_volume+",\"spreadPoints\":"+(string)rate.spread+",\"isCurrent\":"+(current ? "true" : "false")+"}";
}

void SendLatestBars(const string request_id,const string payload)
{
   string symbol,timeframe,raw; ENUM_TIMEFRAMES period;
   if(!TryBarRequest("GetLatestBars",request_id,payload,symbol,timeframe,period)) return;
   if(!GetTopLevelRaw(payload,"count",raw)) { SendError("GetLatestBars",request_id,"InvalidRequest","count is required.",false); return; }
   const int count=(int)StringToInteger(raw); if(count<1 || count>1500) { SendError("GetLatestBars",request_id,"InvalidRequest","count must be between 1 and 1500.",false); return; }
   MqlRates rates[]; ArraySetAsSeries(rates,false); const int copied=CopyRates(symbol,period,0,count+1,rates);
   if(copied<=0) { SendError("GetLatestBars",request_id,"HistoryNotReady","MT5 history is not ready.",true); return; }
   if((bool)SeriesInfoInteger(symbol,period,SERIES_SYNCHRONIZED)==false) { SendError("GetLatestBars",request_id,"HistoryNotReady","MT5 history is not synchronized yet.",true); return; }
   string result="["; for(int i=0;i<copied;i++) { if(i>0) result+=","; result+=RateJson(symbol,timeframe,rates[i],i==copied-1); } result+="]"; SendResponse("GetLatestBars",request_id,result);
}

void SendBarsRange(const string request_id,const string payload)
{
   string symbol,timeframe,start_raw,end_raw; ENUM_TIMEFRAMES period;
   if(!TryBarRequest("GetBarsRange",request_id,payload,symbol,timeframe,period)) return;
   if(!GetTopLevelRaw(payload,"startUnixSeconds",start_raw) || !GetTopLevelRaw(payload,"endUnixSeconds",end_raw)) { SendError("GetBarsRange",request_id,"InvalidRequest","range is required.",false); return; }
   const long start=StringToInteger(start_raw), end=StringToInteger(end_raw); if(start>=end) { SendError("GetBarsRange",request_id,"InvalidRequest","start must be before end.",false); return; }
   MqlRates rates[]; ArraySetAsSeries(rates,false); const int copied=CopyRates(symbol,period,(datetime)start,(datetime)end,rates);
   if(copied<0) { SendError("GetBarsRange",request_id,"HistoryNotReady","MT5 history is not ready.",true); return; }
   if((bool)SeriesInfoInteger(symbol,period,SERIES_SYNCHRONIZED)==false) { SendError("GetBarsRange",request_id,"HistoryNotReady","MT5 history is not synchronized yet.",true); return; }
   string result="["; for(int i=0;i<copied;i++) { if(i>0) result+=","; result+=RateJson(symbol,timeframe,rates[i],false); } result+="]"; SendResponse("GetBarsRange",request_id,result);
}

void SendBarSnapshot(const string request_id,const string payload)
{
   string symbol,timeframe; ENUM_TIMEFRAMES period;
   if(!TryBarRequest("GetBarSnapshot",request_id,payload,symbol,timeframe,period)) return;
   MqlRates rates[]; ArraySetAsSeries(rates,false); const int copied=CopyRates(symbol,period,0,2,rates);
   if(copied<2) { SendError("GetBarSnapshot",request_id,"HistoryNotReady","MT5 history is not ready.",true); return; }
   if((bool)SeriesInfoInteger(symbol,period,SERIES_SYNCHRONIZED)==false) { SendError("GetBarSnapshot",request_id,"HistoryNotReady","MT5 history is not synchronized yet.",true); return; }
   MqlTick tick; if(!SymbolInfoTick(symbol,tick)) { SendError("GetBarSnapshot",request_id,"SymbolUnavailable","A current symbol tick is unavailable.",true); return; }
   const string event_time=IsoUtcMilliseconds(tick.time_msc);
   if(tick.bid<=0.0 || tick.ask<=0.0 || tick.ask<tick.bid) { SendError("GetBarSnapshot",request_id,"SymbolUnavailable","A valid current quote is unavailable.",true); return; }
   const string result="{\"brokerSymbol\":\""+JsonEscape(symbol)+"\",\"timeframe\":\""+JsonEscape(timeframe)+"\",\"eventTimeUtc\":\""+event_time+"\",\"previousClosed\":"+RateJson(symbol,timeframe,rates[copied-2],false)+",\"current\":"+RateJson(symbol,timeframe,rates[copied-1],true)+",\"bid\":"+Number(tick.bid)+",\"ask\":"+Number(tick.ask)+"}";
   SendResponse("GetBarSnapshot",request_id,result);
}

bool TryCalculationRequest(const string operation,const string request_id,const string payload,string &symbol,string &direction,double &volume,double &open_price,double &close_price,const bool requires_close,ENUM_ORDER_TYPE &order_type)
{
   string volume_raw,open_raw,close_raw;
   if(!GetTopLevelString(payload,"brokerSymbol",symbol) || !GetTopLevelString(payload,"direction",direction) || !GetTopLevelRaw(payload,"volumeLots",volume_raw) || !GetTopLevelRaw(payload,"openPrice",open_raw) || (requires_close && !GetTopLevelRaw(payload,"closePrice",close_raw))) { SendError(operation,request_id,"InvalidRequest","symbol, direction, volume and price are required.",false); return false; }
   volume=StringToDouble(volume_raw); open_price=StringToDouble(open_raw); close_price=requires_close ? StringToDouble(close_raw) : 0.0;
   if(direction=="Long") order_type=ORDER_TYPE_BUY; else if(direction=="Short") order_type=ORDER_TYPE_SELL; else { SendError(operation,request_id,"InvalidRequest","Direction must be Long or Short.",false); return false; }
   bool custom=false;
   if(!SymbolExist(symbol,custom) || !((bool)SymbolInfoInteger(symbol,SYMBOL_SELECT))) { SendError(operation,request_id,"NotFound","The requested symbol was not found in Market Watch.",false); return false; }
   if(volume<=0.0 || open_price<=0.0 || (requires_close && close_price<=0.0)) { SendError(operation,request_id,"InvalidRequest","Volume and prices must be positive.",false); return false; }
   double minimum,maximum,step;
   if(!SymbolInfoDouble(symbol,SYMBOL_VOLUME_MIN,minimum) || !SymbolInfoDouble(symbol,SYMBOL_VOLUME_MAX,maximum) || !SymbolInfoDouble(symbol,SYMBOL_VOLUME_STEP,step) || volume<minimum-0.00000001 || volume>maximum+0.00000001 || step<=0.0) { SendError(operation,request_id,"InvalidVolume","The requested lot volume is outside the symbol limits.",false); return false; }
   const double steps=(volume-minimum)/step;
   if(MathAbs(steps-MathRound(steps))>0.000001) { SendError(operation,request_id,"InvalidVolume","The requested lot volume does not conform to the symbol volume step.",false); return false; }
   return true;
}

void SendCalculatedMargin(const string request_id,const string payload)
{
   string symbol,direction; double volume,open_price,close_price,margin; ENUM_ORDER_TYPE order_type;
   if(!TryCalculationRequest("CalculateMargin",request_id,payload,symbol,direction,volume,open_price,close_price,false,order_type)) return;
   ResetLastError();
   if(!OrderCalcMargin(order_type,symbol,volume,open_price,margin) || margin<0.0) { SendError("CalculateMargin",request_id,"CalculationFailed","OrderCalcMargin failed.",false); return; }
   const string result="{\"brokerSymbol\":\""+JsonEscape(symbol)+"\",\"direction\":\""+direction+"\",\"volumeLots\":"+Number(volume)+",\"openPrice\":"+Number(open_price)+",\"requiredMargin\":"+Number(margin)+",\"accountCurrency\":\""+JsonEscape(AccountInfoString(ACCOUNT_CURRENCY))+"\"}";
   SendResponse("CalculateMargin",request_id,result);
}

void SendCalculatedProfit(const string request_id,const string payload)
{
   string symbol,direction; double volume,open_price,close_price,profit; ENUM_ORDER_TYPE order_type;
   if(!TryCalculationRequest("CalculateProfit",request_id,payload,symbol,direction,volume,open_price,close_price,true,order_type)) return;
   ResetLastError();
   if(!OrderCalcProfit(order_type,symbol,volume,open_price,close_price,profit)) { SendError("CalculateProfit",request_id,"CalculationFailed","OrderCalcProfit failed.",false); return; }
   const string result="{\"brokerSymbol\":\""+JsonEscape(symbol)+"\",\"direction\":\""+direction+"\",\"volumeLots\":"+Number(volume)+",\"openPrice\":"+Number(open_price)+",\"closePrice\":"+Number(close_price)+",\"profit\":"+Number(profit)+",\"accountCurrency\":\""+JsonEscape(AccountInfoString(ACCOUNT_CURRENCY))+"\"}";
   SendResponse("CalculateProfit",request_id,result);
}

bool SendHello()
{
   const string instance_id=TerminalInstanceId();
   const string payload="{\"secret\":\""+JsonEscape(InpHandshakeSecret)+"\",\"clientVersion\":\"EMA-Bot-MT5-Bridge/2\",\"terminalInstanceId\":\""+JsonEscape(instance_id)+"\",\"accountFingerprint\":\""+JsonEscape(AccountFingerprint())+"\",\"accountServer\":\""+JsonEscape(AccountInfoString(ACCOUNT_SERVER))+"\",\"accountMode\":\""+AccountModeName()+"\",\"accountTradeAllowed\":"+(AccountTradingAllowed() ? "true" : "false")+",\"expertTradeAllowed\":"+(ExpertTradingAllowed() ? "true" : "false")+"}";
   return SendEnvelope("Hello","Hello","null",payload);
}

bool AccountTradingAllowed() { return (bool)AccountInfoInteger(ACCOUNT_TRADE_ALLOWED) && (bool)TerminalInfoInteger(TERMINAL_TRADE_ALLOWED); }
bool ExpertTradingAllowed() { return (bool)MQLInfoInteger(MQL_TRADE_ALLOWED); }
string AccountFingerprint()
{
   const string value=(string)AccountInfoInteger(ACCOUNT_LOGIN)+"|"+AccountInfoString(ACCOUNT_SERVER); ulong hash=1469598103934665603;
   for(int i=0;i<StringLen(value);i++) { hash^=(ulong)StringGetCharacter(value,i); hash*=1099511628211; }
   return StringFormat("mt5-%I64X",hash);
}
bool DemoExecutionAllowed()
{
   return InpEnableDemoExecution && AccountModeName()=="Demo" && AccountTradingAllowed() && ExpertTradingAllowed() && InpMagicNumber>0 && InpExpectedAccountFingerprint==AccountFingerprint() && InpExpectedServer==AccountInfoString(ACCOUNT_SERVER);
}
void SendExecutionAccount(const string request_id)
{
   const string payload="{\"accountFingerprint\":\""+JsonEscape(AccountFingerprint())+"\",\"server\":\""+JsonEscape(AccountInfoString(ACCOUNT_SERVER))+"\",\"tradeMode\":\""+AccountModeName()+"\",\"accountTradeAllowed\":"+(AccountTradingAllowed() ? "true" : "false")+",\"expertTradeAllowed\":"+(ExpertTradingAllowed() ? "true" : "false")+",\"demoExecutionEnabled\":"+(InpEnableDemoExecution ? "true" : "false")+",\"demoExecutionAllowed\":"+(DemoExecutionAllowed() ? "true" : "false")+"}"; SendResponse("GetExecutionAccount",request_id,payload);
}
bool TryExecutionRequest(const string operation,const string request_id,const string payload,string &symbol,string &side,double &volume,double &sl,double &tp,long &magic,string &marker,long &position)
{
   string raw; sl=0.0; tp=0.0; position=0;
   if(!DemoExecutionAllowed()) { SendError(operation,request_id,"DemoSafetyGate","Demo execution is disabled or the expected Demo account safety checks failed.",false); return false; }
   if(!GetTopLevelString(payload,"brokerSymbol",symbol) || !GetTopLevelString(payload,"side",side) || !GetTopLevelRaw(payload,"volumeLots",raw) || !GetTopLevelRaw(payload,"magicNumber",raw) || !GetTopLevelString(payload,"correlationMarker",marker)) { SendError(operation,request_id,"InvalidRequest","Execution request fields are missing.",false); return false; }
   magic=(long)StringToInteger(raw); if(magic!=InpMagicNumber || StringLen(marker)==0 || StringLen(marker)>31) { SendError(operation,request_id,"OwnershipRejected","Magic number or correlation marker is invalid.",false); return false; }
   if(!GetTopLevelRaw(payload,"volumeLots",raw)) return false; volume=StringToDouble(raw);
   if(GetTopLevelRaw(payload,"stopLoss",raw) && raw!="null") sl=StringToDouble(raw); if(GetTopLevelRaw(payload,"takeProfit",raw) && raw!="null") tp=StringToDouble(raw); if(GetTopLevelRaw(payload,"positionTicket",raw) && raw!="null") position=(long)StringToInteger(raw);
   if((side!="Buy" && side!="Sell") || volume<=0.0 || position<0) { SendError(operation,request_id,"InvalidRequest","Side, volume or position ticket is invalid.",false); return false; }
   return true;
}
// After successful OrderSend the deal MUST be selected in history before
// reading DEAL_POSITION_ID.  Without HistoryDealSelect the return value
// is undefined and PositionIdentifier/PositionTicket will be zero.
ulong ResolvePositionIdentifierFromDeal(const ulong deal_ticket)
{
   if(deal_ticket==0) return 0;
   if(!HistoryDealSelect(deal_ticket)) return 0;
   return (ulong)HistoryDealGetInteger(deal_ticket,DEAL_POSITION_ID);
}

void SendSubmitResult(const string operation,const string request_id,const bool accepted,const MqlTradeResult &result,const string symbol,const long magic,const string side)
{
   const ulong pos_id=accepted && result.deal>0 ? ResolvePositionIdentifierFromDeal(result.deal) : 0;
   const ulong current_position_ticket=ResolveCurrentPositionTicket((long)pos_id,symbol,magic,side);
   const bool partial=result.retcode==TRADE_RETCODE_DONE_PARTIAL;
   const string payload="{\"accepted\":"+(accepted ? "true" : "false")+",\"retcode\":\""+(string)result.retcode+"\",\"message\":\""+JsonEscape(result.comment)+"\",\"orderTicket\":"+(result.order>0 ? (string)result.order : "null")+",\"entryDealTicket\":"+(result.deal>0 ? (string)result.deal : "null")+",\"positionIdentifier\":"+(pos_id>0 ? (string)pos_id : "null")+",\"positionTicket\":"+(current_position_ticket>0 ? (string)current_position_ticket : "null")+",\"filledVolumeLots\":"+OptionalNumber(result.volume)+",\"averageFillPrice\":"+OptionalNumber(result.price)+",\"isPartial\":"+(partial ? "true" : "false")+",\"isPositionOpen\":"+(current_position_ticket>0 ? "true" : "false")+"}";
   SendResponse(operation,request_id,payload);
}

void SendCloseResult(const string operation,const string request_id,const bool accepted,const MqlTradeResult &result)
{
   const bool closed=accepted && result.retcode==TRADE_RETCODE_DONE;
   const string payload="{\"accepted\":"+(accepted ? "true" : "false")+",\"retcode\":\""+(string)result.retcode+"\",\"message\":\""+JsonEscape(result.comment)+"\",\"exitDealTicket\":"+(result.deal>0 ? (string)result.deal : "null")+",\"closedVolumeLots\":"+OptionalNumber(result.volume)+",\"averageClosePrice\":"+OptionalNumber(result.price)+",\"isClosed\":"+(closed ? "true" : "false")+"}";
   SendResponse(operation,request_id,payload);
}
bool TradeAccepted(const uint retcode) { return retcode==TRADE_RETCODE_DONE || retcode==TRADE_RETCODE_DONE_PARTIAL; }

// OrderCheck success is the boolean return of OrderCheck itself; it is NEVER
// derived from TradeAccepted(check.retcode).  Only OrderSend acceptance uses
// DONE/DONE_PARTIAL semantics.
void SendOrderCheckResult(const string operation,const string request_id,const bool accepted,const MqlTradeCheckResult &check)
{
   const string payload="{\"accepted\":"+(accepted ? "true" : "false")+",\"retcode\":\""+(string)check.retcode+"\",\"message\":\""+JsonEscape(check.comment)+"\"}";
   SendResponse(operation,request_id,payload);
}

// DEAL_POSITION_ID is a POSITION IDENTIFIER, not necessarily the current
// PositionTicket.  Resolve the current ticket by scanning live positions for
// identifier + magic + symbol + original direction; require exactly one match.
// Returns the matched ticket, or 0 when zero or multiple matches exist.
ulong ResolveCurrentPositionTicket(const long position_id,const string symbol,const long magic,const string side)
{
   if(position_id<=0) return 0;
   ulong matched=0; int count=0;
   const int total=PositionsTotal();
   for(int i=0;i<total;i++)
   {
      const ulong ticket=PositionGetTicket(i); if(ticket==0) continue;
      if((long)PositionGetInteger(POSITION_IDENTIFIER)!=position_id) continue;
      if((long)PositionGetInteger(POSITION_MAGIC)!=magic) continue;
      if(PositionGetString(POSITION_SYMBOL)!=symbol) continue;
      const ENUM_POSITION_TYPE type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      if((type==POSITION_TYPE_BUY && side!="Buy") || (type==POSITION_TYPE_SELL && side!="Sell")) continue;
      matched=ticket; count++;
   }
   return count==1 ? matched : 0;
}

void HandleExecutionOrder(const string operation,const string request_id,const string payload)
{
   string symbol,side,marker; double volume,sl,tp; long magic,position; if(!TryExecutionRequest(operation,request_id,payload,symbol,side,volume,sl,tp,magic,marker,position)) return;
   MqlTick tick; if(!SymbolInfoTick(symbol,tick)) { SendError(operation,request_id,"SymbolUnavailable","No current quote is available.",true); return; }
   MqlTradeRequest request={}; MqlTradeCheckResult check={}; MqlTradeResult result={}; request.action=TRADE_ACTION_DEAL; request.symbol=symbol; request.magic=(ulong)magic; request.comment=marker; request.deviation=20;
   request.volume=volume; request.type=side=="Buy" ? ORDER_TYPE_BUY : ORDER_TYPE_SELL; request.price=request.type==ORDER_TYPE_BUY ? tick.ask : tick.bid; request.sl=sl; request.tp=tp;
   const bool checked=OrderCheck(request,check);
   if(!checked) { SendOrderCheckResult(operation,request_id,false,check); return; }
   if(operation=="OrderCheck") { SendOrderCheckResult(operation,request_id,true,check); return; }
   const bool sent=OrderSend(request,result); const bool accepted=sent && TradeAccepted(result.retcode); SendSubmitResult(operation,request_id,accepted,result,symbol,magic,side);
}

// Exact native close ownership: the persisted PositionTicket plus its
// PositionIdentifier, magic, symbol and original side.  POSITION_COMMENT is
// never an ownership key, so a truncated or legacy 36-character correlation
// comment cannot block an exact-ticket close.
void HandleClosePosition(const string request_id,const string payload)
{
   string symbol,side,position_raw,identifier_raw,magic_raw;
   if(!DemoExecutionAllowed()) { SendError("ClosePosition",request_id,"DemoSafetyGate","Demo execution is disabled or the expected Demo account safety checks failed.",false); return; }
   if(!GetTopLevelRaw(payload,"positionTicket",position_raw) || !GetTopLevelRaw(payload,"positionIdentifier",identifier_raw) || !GetTopLevelRaw(payload,"magicNumber",magic_raw) || !GetTopLevelString(payload,"brokerSymbol",symbol) || !GetTopLevelString(payload,"side",side) || identifier_raw=="null") { SendError("ClosePosition",request_id,"InvalidRequest","Exact close ownership fields are required.",false); return; }
   const long position=(long)StringToInteger(position_raw);
   const long identifier=(long)StringToInteger(identifier_raw);
   const long magic=(long)StringToInteger(magic_raw);
   if(magic!=InpMagicNumber || position<=0 || identifier<=0 || (side!="Buy" && side!="Sell")) { SendError("ClosePosition",request_id,"InvalidRequest","Close ownership fields are invalid.",false); return; }
   if(!PositionSelectByTicket((ulong)position)) { SendError("ClosePosition",request_id,"OwnershipRejected","The exact owned position ticket was not found.",false); return; }
   const long actual_identifier=(long)PositionGetInteger(POSITION_IDENTIFIER);
   const long actual_magic=(long)PositionGetInteger(POSITION_MAGIC);
   const string actual_symbol=PositionGetString(POSITION_SYMBOL);
   const ENUM_POSITION_TYPE type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   const bool direction_agrees=(type==POSITION_TYPE_BUY && side=="Buy") || (type==POSITION_TYPE_SELL && side=="Sell");
   if(actual_identifier!=identifier || actual_magic!=magic || actual_symbol!=symbol || !direction_agrees) { SendError("ClosePosition",request_id,"OwnershipRejected","The exact position failed native ownership checks.",false); return; }
   MqlTick tick; if(!SymbolInfoTick(symbol,tick)) { SendError("ClosePosition",request_id,"SymbolUnavailable","No current quote is available.",true); return; }
   MqlTradeRequest request={}; MqlTradeCheckResult check={}; MqlTradeResult result={}; request.action=TRADE_ACTION_DEAL; request.symbol=symbol; request.magic=(ulong)magic; request.deviation=20; request.position=(ulong)position; request.volume=PositionGetDouble(POSITION_VOLUME); request.type=type==POSITION_TYPE_BUY ? ORDER_TYPE_SELL : ORDER_TYPE_BUY; request.price=request.type==ORDER_TYPE_BUY ? tick.ask : tick.bid;
   const bool checked=OrderCheck(request,check);
   if(!checked) { MqlTradeResult failed={}; failed.retcode=check.retcode; failed.comment=check.comment; SendCloseResult("ClosePosition",request_id,false,failed); return; }
   const bool sent=OrderSend(request,result); const bool accepted=sent && TradeAccepted(result.retcode); SendCloseResult("ClosePosition",request_id,accepted,result);
}

// Protection modification is purpose-specific and exact-position-only.  It never
// uses POSITION_COMMENT or a marker: ticket + identifier + magic + symbol + original
// side must all match the currently-open native position before TRADE_ACTION_SLTP.
// An accepted OrderSend is never retrospectively rewritten as a rejection.  Missing
// immediate proof is deliberately returned as accepted with null evidence so .NET
// reconciles the exact position without issuing another SL/TP write.
void SendModifyProtectionResult(const string request_id,const bool accepted,const MqlTradeResult &result,const ulong position,const long identifier,const string symbol,const long magic,const string side)
{
   if(!accepted)
   {
      const string failed="{\"accepted\":false,\"retcode\":\""+(string)result.retcode+"\",\"message\":\""+JsonEscape(result.comment)+"\",\"positionTicket\":null,\"positionIdentifier\":null,\"stopLoss\":null,\"takeProfit\":null}";
      SendResponse("ModifyPositionProtection",request_id,failed); return;
   }
   if(!PositionSelectByTicket(position)) { SendResponse("ModifyPositionProtection",request_id,"{\"accepted\":true,\"retcode\":\""+(string)result.retcode+"\",\"message\":\""+JsonEscape(result.comment)+"\",\"positionTicket\":null,\"positionIdentifier\":null,\"stopLoss\":null,\"takeProfit\":null}"); return; }
   const long actual_identifier=(long)PositionGetInteger(POSITION_IDENTIFIER);
   const long actual_magic=(long)PositionGetInteger(POSITION_MAGIC);
   const string actual_symbol=PositionGetString(POSITION_SYMBOL);
   const ENUM_POSITION_TYPE type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   const bool direction_agrees=(type==POSITION_TYPE_BUY && side=="Buy") || (type==POSITION_TYPE_SELL && side=="Sell");
   if(actual_identifier!=identifier || actual_magic!=magic || actual_symbol!=symbol || !direction_agrees) { SendResponse("ModifyPositionProtection",request_id,"{\"accepted\":true,\"retcode\":\""+(string)result.retcode+"\",\"message\":\""+JsonEscape(result.comment)+"\",\"positionTicket\":null,\"positionIdentifier\":null,\"stopLoss\":null,\"takeProfit\":null}"); return; }
   const double sl=PositionGetDouble(POSITION_SL),tp=PositionGetDouble(POSITION_TP);
   const string response="{\"accepted\":true,\"retcode\":\""+(string)result.retcode+"\",\"message\":\""+JsonEscape(result.comment)+"\",\"positionTicket\":"+(string)position+",\"positionIdentifier\":"+(string)actual_identifier+",\"stopLoss\":"+OptionalNumber(sl)+",\"takeProfit\":"+OptionalNumber(tp)+"}";
   SendResponse("ModifyPositionProtection",request_id,response);
}

void HandleModifyPositionProtection(const string request_id,const string payload)
{
   string symbol,side,ticket_raw,identifier_raw,magic_raw,sl_raw,tp_raw;
   if(!DemoExecutionAllowed()) { SendError("ModifyPositionProtection",request_id,"DemoSafetyGate","Demo execution is disabled or the expected Demo account safety checks failed.",false); return; }
   if(!GetTopLevelRaw(payload,"positionTicket",ticket_raw) || !GetTopLevelRaw(payload,"positionIdentifier",identifier_raw) || !GetTopLevelRaw(payload,"magicNumber",magic_raw) || !GetTopLevelString(payload,"brokerSymbol",symbol) || !GetTopLevelString(payload,"side",side) || !GetTopLevelRaw(payload,"stopLoss",sl_raw) || !GetTopLevelRaw(payload,"takeProfit",tp_raw) || identifier_raw=="null" || sl_raw=="null" || tp_raw=="null") { SendError("ModifyPositionProtection",request_id,"InvalidRequest","Exact ownership and both protection values are required.",false); return; }
   const ulong ticket=(ulong)StringToInteger(ticket_raw);
   const long identifier=(long)StringToInteger(identifier_raw),magic=(long)StringToInteger(magic_raw);
   const double sl=StringToDouble(sl_raw),tp=StringToDouble(tp_raw);
   if(ticket==0 || identifier<=0 || magic!=InpMagicNumber || sl<=0.0 || tp<=0.0 || (side!="Buy" && side!="Sell")) { SendError("ModifyPositionProtection",request_id,"InvalidRequest","Protection ownership or price fields are invalid.",false); return; }
   if(!PositionSelectByTicket(ticket)) { SendError("ModifyPositionProtection",request_id,"OwnershipRejected","The exact owned position ticket was not found.",false); return; }
   const long actual_identifier=(long)PositionGetInteger(POSITION_IDENTIFIER),actual_magic=(long)PositionGetInteger(POSITION_MAGIC);
   const string actual_symbol=PositionGetString(POSITION_SYMBOL);
   const ENUM_POSITION_TYPE type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   const bool direction_agrees=(type==POSITION_TYPE_BUY && side=="Buy") || (type==POSITION_TYPE_SELL && side=="Sell");
   if(actual_identifier!=identifier || actual_magic!=magic || actual_symbol!=symbol || !direction_agrees) { SendError("ModifyPositionProtection",request_id,"OwnershipRejected","The exact position failed native ownership checks.",false); return; }
   MqlTick tick; if(!SymbolInfoTick(symbol,tick) || tick.bid<=0.0 || tick.ask<=0.0 || tick.ask<tick.bid) { SendError("ModifyPositionProtection",request_id,"SymbolUnavailable","No valid current quote is available.",true); return; }
   long stops=0,freeze=0; double point=0.0;
   if(!SymbolInfoInteger(symbol,SYMBOL_TRADE_STOPS_LEVEL,stops) || !SymbolInfoInteger(symbol,SYMBOL_TRADE_FREEZE_LEVEL,freeze) || !SymbolInfoDouble(symbol,SYMBOL_POINT,point) || point<=0.0) { SendError("ModifyPositionProtection",request_id,"SymbolUnavailable","Native broker protection constraints are unavailable.",true); return; }
   double tick_size=0.0;
   if(!SymbolInfoDouble(symbol,SYMBOL_TRADE_TICK_SIZE,tick_size) || tick_size<=0.0) tick_size=point;
   if(tick_size<=0.0) { SendError("ModifyPositionProtection",request_id,"SymbolUnavailable","The native broker price grid is unavailable.",true); return; }
   const double grid_tolerance=tick_size/1000000.0;
   const bool grid_valid=MathAbs(sl-MathRound(sl/tick_size)*tick_size)<=grid_tolerance && MathAbs(tp-MathRound(tp/tick_size)*tick_size)<=grid_tolerance;
   if(!grid_valid) { SendError("ModifyPositionProtection",request_id,"InvalidProtection","Protection values are not aligned to the native broker price grid.",false); return; }
   const double stop_distance=(double)stops*point,freeze_distance=(double)freeze*point;
   const bool side_valid=type==POSITION_TYPE_BUY ? sl<tick.bid && tp>tick.ask : sl>tick.ask && tp<tick.bid;
   const bool stops_valid=type==POSITION_TYPE_BUY ? tick.bid-sl>=stop_distance && tp-tick.ask>=stop_distance : sl-tick.ask>=stop_distance && tick.bid-tp>=stop_distance;
   const bool freeze_valid=type==POSITION_TYPE_BUY ? tick.bid-sl>=freeze_distance && tp-tick.ask>=freeze_distance : sl-tick.ask>=freeze_distance && tick.bid-tp>=freeze_distance;
   if(!side_valid) { SendError("ModifyPositionProtection",request_id,"InvalidProtection","Protection values are on the invalid side of the current executable quote.",false); return; }
   if(!stops_valid) { SendError("ModifyPositionProtection",request_id,"StopsLevel","Protection values violate the broker stops level.",false); return; }
   if(!freeze_valid) { SendError("ModifyPositionProtection",request_id,"FreezeLevel","Protection values violate the broker freeze level.",false); return; }
   // Re-read exact native ownership and protection immediately before the write.
   // This closes the race where another actor changed the position after the first
   // ownership read: protection management is monotonic at the broker boundary.
   if(!PositionSelectByTicket(ticket)) { SendError("ModifyPositionProtection",request_id,"OwnershipRejected","The exact owned position ticket was not found before modification.",false); return; }
   const long native_identifier=(long)PositionGetInteger(POSITION_IDENTIFIER),native_magic=(long)PositionGetInteger(POSITION_MAGIC);
   const string native_symbol=PositionGetString(POSITION_SYMBOL);
   const ENUM_POSITION_TYPE native_type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   const bool native_direction_agrees=(native_type==POSITION_TYPE_BUY && side=="Buy") || (native_type==POSITION_TYPE_SELL && side=="Sell");
   if(native_identifier!=identifier || native_magic!=magic || native_symbol!=symbol || !native_direction_agrees) { SendError("ModifyPositionProtection",request_id,"OwnershipRejected","The exact position changed before modification.",false); return; }
   const double current_sl=PositionGetDouble(POSITION_SL),current_tp=PositionGetDouble(POSITION_TP);
   if(current_sl<=0.0 || current_tp<=0.0) { SendError("ModifyPositionProtection",request_id,"InvalidProtection","The exact native position no longer has both protections.",false); return; }
   const bool monotonic_valid=native_type==POSITION_TYPE_BUY ? sl+grid_tolerance>=current_sl && tp+grid_tolerance>=current_tp : sl-grid_tolerance<=current_sl && tp-grid_tolerance<=current_tp;
   if(!monotonic_valid) { SendError("ModifyPositionProtection",request_id,"Monotonicity","Protection modification would weaken the current native protection.",false); return; }
   MqlTradeRequest request={}; MqlTradeCheckResult check={}; MqlTradeResult result={};
   request.action=TRADE_ACTION_SLTP; request.position=ticket; request.symbol=symbol; request.magic=(ulong)magic; request.sl=sl; request.tp=tp;
   if(!OrderCheck(request,check)) { MqlTradeResult failed={}; failed.retcode=check.retcode; failed.comment=check.comment; SendModifyProtectionResult(request_id,false,failed,ticket,identifier,symbol,magic,side); return; }
   const bool sent=OrderSend(request,result); const bool accepted=sent && TradeAccepted(result.retcode);
   SendModifyProtectionResult(request_id,accepted,result,ticket,identifier,symbol,magic,side);
}

// Native GetPosition ownership: exact ticket + identifier + magic + symbol + side.
// The comment is returned only as a diagnostic and never validated.
void SendExecutionPosition(const string request_id,const string payload)
{
   string symbol,side,ticket_raw,identifier_raw,magic_raw;
   if(!GetTopLevelRaw(payload,"positionTicket",ticket_raw) || !GetTopLevelRaw(payload,"positionIdentifier",identifier_raw) || !GetTopLevelString(payload,"brokerSymbol",symbol) || !GetTopLevelString(payload,"side",side) || !GetTopLevelRaw(payload,"magicNumber",magic_raw) || identifier_raw=="null") { SendError("GetPosition",request_id,"InvalidRequest","Exact ticket, identifier, magic, symbol and side are required.",false); return; }
   const ulong ticket=(ulong)StringToInteger(ticket_raw);
   const long identifier=(long)StringToInteger(identifier_raw);
   const long magic=(long)StringToInteger(magic_raw);
   if(ticket<=0 || magic!=InpMagicNumber || identifier<=0 || (side!="Buy" && side!="Sell")) { SendError("GetPosition",request_id,"InvalidRequest","Native position ownership fields are invalid.",false); return; }
   if(!PositionSelectByTicket(ticket)) { SendResponse("GetPosition",request_id,"{\"accepted\":true,\"isClosed\":true}"); return; }
   const long actual_identifier=(long)PositionGetInteger(POSITION_IDENTIFIER);
   const long actual_magic=(long)PositionGetInteger(POSITION_MAGIC);
   const string actual_symbol=PositionGetString(POSITION_SYMBOL);
   const ENUM_POSITION_TYPE type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   const bool direction_agrees=(type==POSITION_TYPE_BUY && side=="Buy") || (type==POSITION_TYPE_SELL && side=="Sell");
   if(actual_identifier!=identifier || actual_magic!=magic || actual_symbol!=symbol || !direction_agrees) { SendError("GetPosition",request_id,"OwnershipRejected","The exact position ticket failed native ownership checks.",false); return; }
   const string actual_side=type==POSITION_TYPE_BUY ? "Buy" : "Sell";
   long digits=0,stops=0,freeze=0; double tick_size=0.0,point=0.0; MqlTick tick;
   SymbolInfoInteger(actual_symbol,SYMBOL_DIGITS,digits); SymbolInfoInteger(actual_symbol,SYMBOL_TRADE_STOPS_LEVEL,stops); SymbolInfoInteger(actual_symbol,SYMBOL_TRADE_FREEZE_LEVEL,freeze); SymbolInfoDouble(actual_symbol,SYMBOL_TRADE_TICK_SIZE,tick_size); SymbolInfoDouble(actual_symbol,SYMBOL_POINT,point);
   const bool quote_ok=SymbolInfoTick(actual_symbol,tick) && tick.bid>0.0 && tick.ask>0.0 && tick.ask>=tick.bid;
   const string result="{\"accepted\":true,\"isClosed\":false,\"positionTicket\":"+(string)ticket+",\"positionIdentifier\":"+(string)actual_identifier+",\"magicNumber\":"+(string)actual_magic+",\"brokerSymbol\":\""+JsonEscape(actual_symbol)+"\",\"side\":\""+actual_side+"\",\"volumeLots\":"+Number(PositionGetDouble(POSITION_VOLUME))+",\"openPrice\":"+Number(PositionGetDouble(POSITION_PRICE_OPEN))+",\"nativeComment\":\""+JsonEscape(PositionGetString(POSITION_COMMENT))+"\",\"stopLoss\":"+OptionalNumber(PositionGetDouble(POSITION_SL))+",\"takeProfit\":"+OptionalNumber(PositionGetDouble(POSITION_TP))+",\"bid\":"+(quote_ok ? Number(tick.bid) : "null")+",\"ask\":"+(quote_ok ? Number(tick.ask) : "null")+",\"digits\":"+(string)digits+",\"tickSize\":"+OptionalNumber(tick_size)+",\"pointSize\":"+OptionalNumber(point)+",\"stopsLevelPoints\":"+(string)stops+",\"freezeLevelPoints\":"+(string)freeze+"}";
   SendResponse("GetPosition",request_id,result);
}

// Exact entry deal lookup: HistoryDealSelect on the persisted ticket, then every
// returned field is an actual broker-read value; request expectations are never
// echoed back as evidence.  The .NET side independently validates the evidence.
void SendExactDeal(const string request_id,const string payload)
{
   string symbol,side,deal_raw,magic_raw,volume_raw,order_raw;
   if(!GetTopLevelRaw(payload,"exactDealTicket",deal_raw) || !GetTopLevelRaw(payload,"magicNumber",magic_raw) || !GetTopLevelRaw(payload,"expectedVolumeLots",volume_raw) || !GetTopLevelString(payload,"brokerSymbol",symbol) || !GetTopLevelString(payload,"side",side)) { SendError("GetExactDeal",request_id,"InvalidRequest","Exact deal ticket and ownership fields are required.",false); return; }
   const long exact_deal=(long)StringToInteger(deal_raw);
   const long magic=(long)StringToInteger(magic_raw);
   const double expected_volume=StringToDouble(volume_raw);
   long expected_order=0;
   if(GetTopLevelRaw(payload,"expectedOrderTicket",order_raw) && order_raw!="null") expected_order=(long)StringToInteger(order_raw);
   if(exact_deal<=0 || magic!=InpMagicNumber || expected_volume<=0.0 || expected_order<0 || (side!="Buy" && side!="Sell")) { SendError("GetExactDeal",request_id,"InvalidRequest","Exact deal fields are invalid.",false); return; }
   if(!HistoryDealSelect((ulong)exact_deal)) { SendError("GetExactDeal",request_id,"DealNotFound","The exact deal was not found in terminal history.",true); return; }
   const ENUM_DEAL_TYPE type=(ENUM_DEAL_TYPE)HistoryDealGetInteger((ulong)exact_deal,DEAL_TYPE);
   if(type!=DEAL_TYPE_BUY && type!=DEAL_TYPE_SELL) { SendError("GetExactDeal",request_id,"InvalidEvidence","The exact deal is not a market deal.",false); return; }
   const double volume=HistoryDealGetDouble((ulong)exact_deal,DEAL_VOLUME);
   const long position_id=(long)HistoryDealGetInteger((ulong)exact_deal,DEAL_POSITION_ID);
   const long actual_order=(long)HistoryDealGetInteger((ulong)exact_deal,DEAL_ORDER);
   const long actual_magic=(long)HistoryDealGetInteger((ulong)exact_deal,DEAL_MAGIC);
   const string deal_side=type==DEAL_TYPE_BUY ? "Buy" : "Sell";
   const string actual_symbol=HistoryDealGetString((ulong)exact_deal,DEAL_SYMBOL);
   const ENUM_DEAL_ENTRY entry=(ENUM_DEAL_ENTRY)HistoryDealGetInteger((ulong)exact_deal,DEAL_ENTRY);
   const double actual_price=HistoryDealGetDouble((ulong)exact_deal,DEAL_PRICE);
   const long actual_time_msc=(long)HistoryDealGetInteger((ulong)exact_deal,DEAL_TIME_MSC);
   const string actual_comment=HistoryDealGetString((ulong)exact_deal,DEAL_COMMENT);
   const string native_reason=DealReasonName((ENUM_DEAL_REASON)HistoryDealGetInteger((ulong)exact_deal,DEAL_REASON));
   const bool is_entry=entry==DEAL_ENTRY_IN;
   const bool is_exit=entry==DEAL_ENTRY_OUT || entry==DEAL_ENTRY_OUT_BY || entry==DEAL_ENTRY_INOUT;
   if(actual_magic!=magic || actual_symbol!=symbol || deal_side!=side || !is_entry || volume<=0.0 || volume>expected_volume || position_id<=0 || (expected_order>0 && actual_order!=expected_order)) { SendError("GetExactDeal",request_id,"OwnershipRejected","The exact deal failed native ownership checks.",false); return; }
   const ulong current_ticket=ResolveCurrentPositionTicket(position_id,actual_symbol,actual_magic,deal_side);
   const string payload_result="{\"dealTicket\":"+(string)exact_deal+",\"orderTicket\":"+(actual_order>0 ? (string)actual_order : "null")+",\"positionIdentifier\":"+(string)position_id+",\"positionTicket\":"+(current_ticket>0 ? (string)current_ticket : "null")+",\"brokerSymbol\":\""+JsonEscape(actual_symbol)+"\",\"side\":\""+deal_side+"\",\"magicNumber\":"+(string)actual_magic+",\"executedVolumeLots\":"+Number(volume)+",\"executionPrice\":"+Number(actual_price)+",\"executedAtUtc\":\""+IsoUtcMilliseconds(actual_time_msc)+"\",\"isEntry\":"+(is_entry ? "true" : "false")+",\"isExit\":"+(is_exit ? "true" : "false")+",\"isPositionOpen\":"+(current_ticket>0 ? "true" : "false")+",\"nativeComment\":\""+JsonEscape(actual_comment)+"\",\"nativeReason\":\""+JsonEscape(native_reason)+"\"}";
   SendResponse("GetExactDeal",request_id,payload_result);
}

// Native position history grouped strictly by the exact trusted PositionIdentifier.
// Used to reconstruct a position manually closed outside EMA-Bot once exact entry
// identity has already been proven; exit deals may carry foreign magic/comment.
void SendPositionHistory(const string request_id,const string payload)
{
   string symbol,side,identifier_raw,entry_deal_raw,magic_raw,volume_raw,from_raw,to_raw;
   if(!GetTopLevelRaw(payload,"positionIdentifier",identifier_raw) || !GetTopLevelRaw(payload,"entryDealTicket",entry_deal_raw) || !GetTopLevelRaw(payload,"magicNumber",magic_raw) || !GetTopLevelRaw(payload,"expectedVolumeLots",volume_raw) || !GetTopLevelString(payload,"brokerSymbol",symbol) || !GetTopLevelString(payload,"side",side) || !GetTopLevelRaw(payload,"fromUnixSeconds",from_raw) || !GetTopLevelRaw(payload,"toUnixSeconds",to_raw)) { SendError("GetPositionHistory",request_id,"InvalidRequest","Native position history fields are required.",false); return; }
   const long identifier=(long)StringToInteger(identifier_raw);
   const long entry_deal=(long)StringToInteger(entry_deal_raw);
   const long magic=(long)StringToInteger(magic_raw);
   const double expected_volume=StringToDouble(volume_raw);
   const long from=(long)StringToInteger(from_raw), to=(long)StringToInteger(to_raw);
   if(identifier<=0 || entry_deal<=0 || magic!=InpMagicNumber || expected_volume<=0.0 || (side!="Buy" && side!="Sell") || from<=0 || to<from || to-from>604800) { SendError("GetPositionHistory",request_id,"InvalidRequest","Native position history bounds or identity fields are invalid.",false); return; }
   // Prove the trusted entry before enumerating any exit.  Manual exit deals may
   // carry another magic/comment, but only after this exact native anchor holds.
   if(!HistoryDealSelect((ulong)entry_deal)) { SendError("GetPositionHistory",request_id,"EntryNotFound","The exact entry deal was not found in terminal history.",true); return; }
   const ENUM_DEAL_TYPE entry_type=(ENUM_DEAL_TYPE)HistoryDealGetInteger((ulong)entry_deal,DEAL_TYPE);
   const ENUM_DEAL_ENTRY entry_semantics=(ENUM_DEAL_ENTRY)HistoryDealGetInteger((ulong)entry_deal,DEAL_ENTRY);
   const string entry_side=entry_type==DEAL_TYPE_BUY ? "Buy" : entry_type==DEAL_TYPE_SELL ? "Sell" : "";
   const long entry_identifier=(long)HistoryDealGetInteger((ulong)entry_deal,DEAL_POSITION_ID);
   const long entry_magic=(long)HistoryDealGetInteger((ulong)entry_deal,DEAL_MAGIC);
   const string entry_symbol=HistoryDealGetString((ulong)entry_deal,DEAL_SYMBOL);
   const double entry_volume=HistoryDealGetDouble((ulong)entry_deal,DEAL_VOLUME);
   if(entry_identifier!=identifier || entry_magic!=magic || entry_symbol!=symbol || entry_side!=side || entry_semantics!=DEAL_ENTRY_IN || entry_volume<=0.0 || entry_volume>expected_volume) { SendError("GetPositionHistory",request_id,"OwnershipRejected","The exact native entry deal failed ownership checks.",false); return; }
   if(!HistorySelect((datetime)from,(datetime)to)) { SendError("GetPositionHistory",request_id,"HistoryUnavailable","MT5 history selection failed.",true); return; }
   string items="["; int matched=0; const int count=HistoryDealsTotal();
   for(int i=0;i<count;i++)
   {
      const ulong deal=HistoryDealGetTicket(i); if(deal==0) continue;
      if((long)HistoryDealGetInteger(deal,DEAL_POSITION_ID)!=identifier) continue;
      const ENUM_DEAL_TYPE type=(ENUM_DEAL_TYPE)HistoryDealGetInteger(deal,DEAL_TYPE); if(type!=DEAL_TYPE_BUY && type!=DEAL_TYPE_SELL) continue;
      const ENUM_DEAL_ENTRY entry=(ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal,DEAL_ENTRY);
      const bool is_entry=entry==DEAL_ENTRY_IN;
      const bool is_exit=entry==DEAL_ENTRY_OUT || entry==DEAL_ENTRY_OUT_BY || entry==DEAL_ENTRY_INOUT;
      const string deal_side=type==DEAL_TYPE_BUY ? "Buy" : "Sell";
      if(matched++>0) items+=",";
      items+="{\"dealTicket\":"+(string)deal+",\"orderTicket\":"+(string)HistoryDealGetInteger(deal,DEAL_ORDER)+",\"positionIdentifier\":"+(string)identifier+",\"brokerSymbol\":\""+JsonEscape(HistoryDealGetString(deal,DEAL_SYMBOL))+"\",\"side\":\""+deal_side+"\",\"magicNumber\":"+(string)HistoryDealGetInteger(deal,DEAL_MAGIC)+",\"executedVolumeLots\":"+Number(HistoryDealGetDouble(deal,DEAL_VOLUME))+",\"executionPrice\":"+Number(HistoryDealGetDouble(deal,DEAL_PRICE))+",\"executedAtUtc\":\""+IsoUtcMilliseconds((long)HistoryDealGetInteger(deal,DEAL_TIME_MSC))+"\",\"entryType\":\""+DealEntryName(entry)+"\",\"isEntry\":"+(is_entry ? "true" : "false")+",\"isExit\":"+(is_exit ? "true" : "false")+",\"nativeComment\":\""+JsonEscape(HistoryDealGetString(deal,DEAL_COMMENT))+"\",\"nativeReason\":\""+JsonEscape(DealReasonName((ENUM_DEAL_REASON)HistoryDealGetInteger(deal,DEAL_REASON)))+"\"}";
   }
   items+="]";
   SendResponse("GetPositionHistory",request_id,"{\"positionIdentifier\":"+(string)identifier+",\"deals\":"+items+"}");
}

void SendExecutionHistory(const string request_id,const string payload)
{
   string client_id,marker,symbol,side,raw,from_raw,to_raw,volume_raw;
   if(!GetTopLevelString(payload,"clientExecutionId",client_id) || !GetTopLevelString(payload,"correlationMarker",marker) || !GetTopLevelString(payload,"brokerSymbol",symbol) || !GetTopLevelString(payload,"side",side) || !GetTopLevelRaw(payload,"magicNumber",raw) || !GetTopLevelRaw(payload,"expectedVolumeLots",volume_raw) || !GetTopLevelRaw(payload,"fromUnixSeconds",from_raw) || !GetTopLevelRaw(payload,"toUnixSeconds",to_raw)) { SendError("GetExecutionHistory",request_id,"InvalidRequest","Bounded execution history request fields are required.",false); return; }
   const long magic=(long)StringToInteger(raw), from=(long)StringToInteger(from_raw), to=(long)StringToInteger(to_raw); const double expected_volume=StringToDouble(volume_raw);
   if(StringLen(client_id)<32 || StringLen(marker)==0 || StringLen(marker)>31 || (side!="Buy" && side!="Sell") || expected_volume<=0.0 || magic!=InpMagicNumber || from<=0 || to<from || to-from>604800) { SendError("GetExecutionHistory",request_id,"InvalidRequest","Execution history bounds or correlation fields are invalid.",false); return; }
   if(!HistorySelect((datetime)from,(datetime)to)) { SendError("GetExecutionHistory",request_id,"HistoryUnavailable","MT5 history selection failed.",true); return; }
   string items="["; int matched=0; const int count=HistoryDealsTotal();
   for(int i=0;i<count;i++)
   {
      const ulong deal=HistoryDealGetTicket(i); if(deal==0) continue;
      if((long)HistoryDealGetInteger(deal,DEAL_MAGIC)!=magic || HistoryDealGetString(deal,DEAL_SYMBOL)!=symbol || HistoryDealGetString(deal,DEAL_COMMENT)!=marker) continue;
      const ENUM_DEAL_TYPE type=(ENUM_DEAL_TYPE)HistoryDealGetInteger(deal,DEAL_TYPE); if(type!=DEAL_TYPE_BUY && type!=DEAL_TYPE_SELL) continue;
      const ENUM_DEAL_ENTRY entry=(ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal,DEAL_ENTRY); const bool is_entry=(entry==DEAL_ENTRY_IN || entry==DEAL_ENTRY_INOUT); const bool is_exit=(entry==DEAL_ENTRY_OUT || entry==DEAL_ENTRY_OUT_BY || entry==DEAL_ENTRY_INOUT);
      const string deal_side=type==DEAL_TYPE_BUY ? "Buy" : "Sell"; if(is_entry && deal_side!=side) continue;
      const double volume=HistoryDealGetDouble(deal,DEAL_VOLUME); if(volume<=0.0 || volume>expected_volume+0.00000001) continue;
       const long position_id=(long)HistoryDealGetInteger(deal,DEAL_POSITION_ID); const long order_ticket=(long)HistoryDealGetInteger(deal,DEAL_ORDER); const long time_msc=(long)HistoryDealGetInteger(deal,DEAL_TIME_MSC); const ulong position_ticket=ResolveCurrentPositionTicket(position_id,symbol,magic,deal_side);
      if(matched++>0) items+=",";
       items+="{\"orderTicket\":"+(order_ticket>0 ? (string)order_ticket : "null")+",\"dealTicket\":"+(string)deal+",\"positionIdentifier\":"+(position_id>0 ? (string)position_id : "null")+",\"positionTicket\":"+(position_ticket>0 ? (string)position_ticket : "null")+",\"brokerSymbol\":\""+JsonEscape(symbol)+"\",\"side\":\""+deal_side+"\",\"magicNumber\":"+(string)magic+",\"correlationMarker\":\""+JsonEscape(marker)+"\",\"executedVolumeLots\":"+Number(volume)+",\"executionPrice\":"+Number(HistoryDealGetDouble(deal,DEAL_PRICE))+",\"executedAtUtc\":\""+IsoUtcMilliseconds(time_msc)+"\",\"entryType\":\""+DealEntryName(entry)+"\",\"dealState\":\"HistoryDeal\",\"isEntry\":"+(is_entry ? "true" : "false")+",\"isExit\":"+(is_exit ? "true" : "false")+",\"isPartial\":"+(entry==DEAL_ENTRY_INOUT ? "true" : "false")+",\"nativeReason\":\""+JsonEscape(DealReasonName((ENUM_DEAL_REASON)HistoryDealGetInteger(deal,DEAL_REASON)))+"\"}";
   }
   items+="]"; SendResponse("GetExecutionHistory",request_id,"{\"evidence\":"+items+"}");
}
string DealEntryName(const ENUM_DEAL_ENTRY entry)
{
   if(entry==DEAL_ENTRY_IN) return "Entry"; if(entry==DEAL_ENTRY_OUT) return "Exit"; if(entry==DEAL_ENTRY_INOUT) return "InOut"; if(entry==DEAL_ENTRY_OUT_BY) return "OutBy"; return "Unknown";
}

// DEAL_REASON is native historical evidence.  It never derives from a comment,
// expected request, or inferred price relationship.
string DealReasonName(const ENUM_DEAL_REASON reason)
{
   if(reason==DEAL_REASON_CLIENT) return "Client";
   if(reason==DEAL_REASON_MOBILE) return "Mobile";
   if(reason==DEAL_REASON_WEB) return "Web";
   if(reason==DEAL_REASON_EXPERT) return "Expert";
   if(reason==DEAL_REASON_SL) return "SL";
   if(reason==DEAL_REASON_TP) return "TP";
   if(reason==DEAL_REASON_SO) return "StopOut";
   if(reason==DEAL_REASON_ROLLOVER) return "Rollover";
   if(reason==DEAL_REASON_VMARGIN) return "VMargin";
   if(reason==DEAL_REASON_SPLIT) return "Split";
   if(reason==DEAL_REASON_CORPORATE_ACTION) return "CorporateAction";
   return "Unknown:"+(string)(long)reason;
}

void SendHeartbeat()
{
   SendEnvelope("Heartbeat","Heartbeat","null","{\"clientTimeUtc\":\""+UtcNow()+"\"}");
}

void SendAccount(const string request_id)
{
   const string payload="{\"login\":"+(string)AccountInfoInteger(ACCOUNT_LOGIN)+",\"server\":\""+JsonEscape(AccountInfoString(ACCOUNT_SERVER))+"\",\"currency\":\""+JsonEscape(AccountInfoString(ACCOUNT_CURRENCY))+"\",\"balance\":"+Number(AccountInfoDouble(ACCOUNT_BALANCE))+",\"equity\":"+Number(AccountInfoDouble(ACCOUNT_EQUITY))+",\"margin\":"+Number(AccountInfoDouble(ACCOUNT_MARGIN))+",\"freeMargin\":"+Number(AccountInfoDouble(ACCOUNT_MARGIN_FREE))+",\"marginLevel\":"+Number(AccountInfoDouble(ACCOUNT_MARGIN_LEVEL))+",\"tradeMode\":\""+AccountModeName()+"\"}";
   SendResponse("GetAccount",request_id,payload);
}

void SendInstruments(const string request_id)
{
   string items="[";
   const int count=SymbolsTotal(true);
   for(int i=0;i<count;i++)
   {
      const string symbol=SymbolName(i,true);
      if(symbol=="") continue;
      const string item=InstrumentJson(symbol);
      if(item=="") continue;
      if(StringLen(items)>1) items+=",";
      items+=item;
   }
   items+="]";
   SendResponse("GetInstruments",request_id,items);
}

void SendInstrument(const string request_id,const string symbol)
{
   bool custom=false;
   if(!SymbolExist(symbol,custom) || !((bool)SymbolInfoInteger(symbol,SYMBOL_SELECT))) { SendError("GetInstrument",request_id,"NotFound","The requested symbol was not found in Market Watch.",false); return; }
   const string item=InstrumentJson(symbol);
   if(item=="") { SendError("GetInstrument",request_id,"SymbolUnavailable","The requested symbol data is unavailable.",true); return; }
   SendResponse("GetInstrument",request_id,item);
}

void SendQuote(const string request_id,const string symbol)
{
   bool custom=false;
   if(!SymbolExist(symbol,custom) || !((bool)SymbolInfoInteger(symbol,SYMBOL_SELECT))) { SendError("GetQuote",request_id,"NotFound","The requested symbol was not found in Market Watch.",false); return; }
   MqlTick tick;
   if(!SymbolInfoTick(symbol,tick) || tick.bid<=0.0 || tick.ask<=0.0 || tick.ask<tick.bid) { SendError("GetQuote",request_id,"SymbolUnavailable","A valid current quote is unavailable.",true); return; }
   const string last=tick.last>0.0 ? Number(tick.last) : "null";
   const string volume=tick.volume_real>0.0 ? Number(tick.volume_real) : "null";
   const string payload="{\"brokerSymbol\":\""+JsonEscape(symbol)+"\",\"timeUtc\":\""+IsoUtcMilliseconds(tick.time_msc)+"\",\"bid\":"+Number(tick.bid)+",\"ask\":"+Number(tick.ask)+",\"last\":"+last+",\"volume\":"+volume+"}";
   SendResponse("GetQuote",request_id,payload);
}

string InstrumentJson(const string symbol)
{
   long digits,stops,freeze,trade_mode,selected,visible;
   double point,contract_size,volume_min,volume_max,volume_step,tick_size,tick_profit,tick_loss,volume_limit;
   if(!SymbolInfoInteger(symbol,SYMBOL_DIGITS,digits) || !SymbolInfoDouble(symbol,SYMBOL_POINT,point) || !SymbolInfoDouble(symbol,SYMBOL_TRADE_CONTRACT_SIZE,contract_size) || !SymbolInfoDouble(symbol,SYMBOL_VOLUME_MIN,volume_min) || !SymbolInfoDouble(symbol,SYMBOL_VOLUME_MAX,volume_max) || !SymbolInfoDouble(symbol,SYMBOL_VOLUME_STEP,volume_step)) return "";
   SymbolInfoInteger(symbol,SYMBOL_TRADE_STOPS_LEVEL,stops); SymbolInfoInteger(symbol,SYMBOL_TRADE_FREEZE_LEVEL,freeze); SymbolInfoInteger(symbol,SYMBOL_TRADE_MODE,trade_mode); SymbolInfoInteger(symbol,SYMBOL_SELECT,selected); SymbolInfoInteger(symbol,SYMBOL_VISIBLE,visible);
   SymbolInfoDouble(symbol,SYMBOL_TRADE_TICK_SIZE,tick_size); SymbolInfoDouble(symbol,SYMBOL_TRADE_TICK_VALUE_PROFIT,tick_profit); SymbolInfoDouble(symbol,SYMBOL_TRADE_TICK_VALUE_LOSS,tick_loss); SymbolInfoDouble(symbol,SYMBOL_VOLUME_LIMIT,volume_limit);
   string display=SymbolInfoString(symbol,SYMBOL_DESCRIPTION); if(display=="") display=symbol;
   const string spec="{\"brokerSymbol\":\""+JsonEscape(symbol)+"\",\"displaySymbol\":\""+JsonEscape(display)+"\",\"assetClass\":\"Unknown\",\"digits\":"+(string)digits+",\"pointSize\":"+Number(point)+",\"contractSize\":"+Number(contract_size)+",\"volumeMin\":"+Number(volume_min)+",\"volumeMax\":"+Number(volume_max)+",\"volumeStep\":"+Number(volume_step)+",\"tickSize\":"+OptionalNumber(tick_size)+",\"tickValueProfit\":"+OptionalNumber(tick_profit)+",\"tickValueLoss\":"+OptionalNumber(tick_loss)+",\"volumeLimit\":"+OptionalNumber(volume_limit)+",\"stopsLevelPoints\":"+(string)stops+",\"freezeLevelPoints\":"+(string)freeze+",\"currencyBase\":\""+JsonEscape(SymbolInfoString(symbol,SYMBOL_CURRENCY_BASE))+"\",\"currencyProfit\":\""+JsonEscape(SymbolInfoString(symbol,SYMBOL_CURRENCY_PROFIT))+"\",\"currencyMargin\":\""+JsonEscape(SymbolInfoString(symbol,SYMBOL_CURRENCY_MARGIN))+"\"}";
   return "{\"spec\":"+spec+",\"description\":\""+JsonEscape(SymbolInfoString(symbol,SYMBOL_DESCRIPTION))+"\",\"path\":\""+JsonEscape(SymbolInfoString(symbol,SYMBOL_PATH))+"\",\"isSelected\":"+(selected!=0 ? "true" : "false")+",\"isVisible\":"+(visible!=0 ? "true" : "false")+",\"tradeMode\":\""+TradeModeName((ENUM_SYMBOL_TRADE_MODE)trade_mode)+"\"}";
}

void SendResponse(const string operation,const string request_id,const string payload) { SendEnvelope("Response",operation,request_id,payload); }
void SendError(const string operation,const string request_id,const string code,const string message,const bool retryable)
{
   SendEnvelope("Error",operation,request_id,"{\"code\":\""+JsonEscape(code)+"\",\"message\":\""+JsonEscape(message)+"\",\"retryable\":"+(retryable ? "true" : "false")+",\"nativeCode\":"+(string)GetLastError()+"}");
}

bool SendEnvelope(const string kind,const string operation,const string request_id,const string payload)
{
   const string json="{\"protocolVersion\":"+(string)PROTOCOL_VERSION+",\"kind\":\""+kind+"\",\"operation\":\""+operation+"\",\"requestId\":"+request_id+",\"sentAtUtc\":\""+UtcNow()+"\",\"payload\":"+payload+"}";
   uchar bytes[];
   int count=StringToCharArray(json,bytes,0,WHOLE_ARRAY,CP_UTF8);
   if(count<=0) { ClosePipe(); return false; }
   if(bytes[count-1]==0) count--;
   if(count<=0 || (uint)count>InpMaxFrameBytes) { ClosePipe(); return false; }
   uchar header[4];
   header[0]=(uchar)(count & 0xFF); header[1]=(uchar)((count>>8)&0xFF); header[2]=(uchar)((count>>16)&0xFF); header[3]=(uchar)((count>>24)&0xFF);
   if(!PrepareWrite() || FileWriteArray(g_pipe,header,0,4)!=4 || FileWriteArray(g_pipe,bytes,0,count)!=count) { ClosePipe(); return false; }
   FileFlush(g_pipe);
   return true;
}

string JsonEscape(const string value)
{
   string result="";
   for(int i=0;i<StringLen(value);i++)
   {
      const ushort c=(ushort)StringGetCharacter(value,i);
      if(c=='\"') result+="\\\"";
      else if(c=='\\') result+="\\\\";
      else if(c==8) result+="\\b";
      else if(c==12) result+="\\f";
      else if(c=='\n') result+="\\n";
      else if(c=='\r') result+="\\r";
      else if(c=='\t') result+="\\t";
      else if(c<32) result+=StringFormat("\\u%04X",c);
      else result+=ShortToString((short)c);
   }
   return result;
}

bool GetTopLevelInt(const string json,const string key,int &value)
{
   string raw; if(!GetTopLevelRaw(json,key,raw)) return false; value=(int)StringToInteger(raw); return true;
}

bool GetTopLevelString(const string json,const string key,string &value)
{
   string raw; if(!GetTopLevelRaw(json,key,raw) || StringLen(raw)<2 || StringGetCharacter(raw,0)!='\"') return false;
   return DecodeJsonString(raw,value);
}

bool GetTopLevelRaw(const string json,const string key,string &value)
{
   const int length=StringLen(json); int depth=0; bool in_string=false; bool escape=false;
   for(int i=0;i<length;i++)
   {
      const ushort c=(ushort)StringGetCharacter(json,i);
      if(in_string)
      {
         if(escape) { escape=false; continue; }
         if(c=='\\') { escape=true; continue; }
         if(c=='\"') in_string=false;
         continue;
      }
      if(c=='\"')
      {
         if(depth==1)
         {
            int end=i+1; bool local_escape=false;
            for(;end<length;end++) { ushort sc=(ushort)StringGetCharacter(json,end); if(local_escape) { local_escape=false; continue; } if(sc=='\\') { local_escape=true; continue; } if(sc=='\"') break; }
            string candidate=StringSubstr(json,i+1,end-i-1); int colon=SkipWhitespace(json,end+1);
            if(candidate==key && colon<length && StringGetCharacter(json,colon)==':')
            {
               int start=SkipWhitespace(json,colon+1); int finish=JsonValueEnd(json,start);
               if(start<0 || finish<=start) return false; value=StringSubstr(json,start,finish-start); return true;
            }
            i=end; continue;
         }
         in_string=true; continue;
      }
      if(c=='{') depth++; else if(c=='}') depth--;
   }
   return false;
}

int SkipWhitespace(const string text,int index) { while(index<StringLen(text) && (StringGetCharacter(text,index)==' ' || StringGetCharacter(text,index)=='\t' || StringGetCharacter(text,index)=='\r' || StringGetCharacter(text,index)=='\n')) index++; return index; }
int JsonValueEnd(const string text,int start)
{
   if(start>=StringLen(text)) return -1;
   const ushort first=(ushort)StringGetCharacter(text,start);
   if(first=='\"') { bool escape=false; for(int i=start+1;i<StringLen(text);i++) { ushort c=(ushort)StringGetCharacter(text,i); if(escape) { escape=false; continue; } if(c=='\\') { escape=true; continue; } if(c=='\"') return i+1; } return -1; }
   if(first=='{' || first=='[') { const ushort close=first=='{' ? '}' : ']'; int depth=0; bool in_string=false; bool escape=false; for(int i=start;i<StringLen(text);i++) { ushort c=(ushort)StringGetCharacter(text,i); if(in_string) { if(escape) escape=false; else if(c=='\\') escape=true; else if(c=='\"') in_string=false; continue; } if(c=='\"') { in_string=true; continue; } if(c==first) depth++; else if(c==close && --depth==0) return i+1; } return -1; }
   int i=start; while(i<StringLen(text) && StringGetCharacter(text,i)!=',' && StringGetCharacter(text,i)!='}') i++; return i;
}

bool DecodeJsonString(const string raw,string &value)
{
   value=""; const int length=StringLen(raw); if(length<2 || StringGetCharacter(raw,0)!='\"' || StringGetCharacter(raw,length-1)!='\"') return false;
   for(int i=1;i<length-1;i++)
   {
      ushort c=(ushort)StringGetCharacter(raw,i);
      if(c!='\\') { value+=ShortToString((short)c); continue; }
      if(++i>=length-1) return false; c=(ushort)StringGetCharacter(raw,i);
      if(c=='\"' || c=='\\' || c=='/') value+=ShortToString((short)c);
      else if(c=='b') value+=ShortToString(8); else if(c=='f') value+=ShortToString(12); else if(c=='n') value+="\n"; else if(c=='r') value+="\r"; else if(c=='t') value+="\t";
      else return false;
   }
   return true;
}

string Number(const double value) { return DoubleToString(value,10); }
string OptionalNumber(const double value) { return value>0.0 ? Number(value) : "null"; }
string UtcNow() { return IsoUtcSeconds(TimeGMT()); }
string IsoUtcSeconds(const datetime value)
{
   MqlDateTime parts;
   if(!TimeToStruct(value,parts)) return "";
   return StringFormat("%04d-%02d-%02dT%02d:%02d:%02dZ",parts.year,parts.mon,parts.day,parts.hour,parts.min,parts.sec);
}
string IsoUtcMilliseconds(const long unix_milliseconds)
{
   const datetime seconds=(datetime)(unix_milliseconds/1000);
   const long milliseconds=unix_milliseconds%1000;
   MqlDateTime parts;
   if(!TimeToStruct(seconds,parts)) return "";
   return StringFormat("%04d-%02d-%02dT%02d:%02d:%02d.%03dZ",parts.year,parts.mon,parts.day,parts.hour,parts.min,parts.sec,milliseconds);
}
string TerminalInstanceId()
{
   string path=TerminalInfoString(TERMINAL_DATA_PATH); ulong hash=1469598103934665603;
   for(int i=0;i<StringLen(path);i++) { hash^=(ulong)StringGetCharacter(path,i); hash*=1099511628211; }
   return StringFormat("mt5-%I64X",hash);
}
string AccountModeName()
{
   const ENUM_ACCOUNT_TRADE_MODE mode=(ENUM_ACCOUNT_TRADE_MODE)AccountInfoInteger(ACCOUNT_TRADE_MODE);
   if(mode==ACCOUNT_TRADE_MODE_DEMO) return "Demo"; if(mode==ACCOUNT_TRADE_MODE_CONTEST) return "Contest"; if(mode==ACCOUNT_TRADE_MODE_REAL) return "Real"; return "Unknown";
}
string TradeModeName(const ENUM_SYMBOL_TRADE_MODE mode)
{
   if(mode==SYMBOL_TRADE_MODE_DISABLED) return "Disabled"; if(mode==SYMBOL_TRADE_MODE_LONGONLY) return "LongOnly"; if(mode==SYMBOL_TRADE_MODE_SHORTONLY) return "ShortOnly"; if(mode==SYMBOL_TRADE_MODE_CLOSEONLY) return "CloseOnly"; if(mode==SYMBOL_TRADE_MODE_FULL) return "Full"; return "Unknown";
}
