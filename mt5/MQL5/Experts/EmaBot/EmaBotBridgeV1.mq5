#property strict
#property version   "1.00"
#property description "Read-only EMA-Bot MT5 Named Pipe adapter. It contains no strategy or trade execution logic."

input string InpPipeName="ema-bot.mt5.bridge.v1";
input string InpHandshakeSecret="";
input uint   InpPollMilliseconds=100;
input uint   InpHeartbeatSeconds=5;
input uint   InpReconnectMilliseconds=1000;
input uint   InpMaxFrameBytes=1048576;

#define PROTOCOL_VERSION 1
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
   const string payload="{\"secret\":\""+JsonEscape(InpHandshakeSecret)+"\",\"clientVersion\":\"EMA-Bot-MT5-Bridge/1\",\"terminalInstanceId\":\""+JsonEscape(instance_id)+"\",\"terminalName\":\""+JsonEscape(TerminalInfoString(TERMINAL_NAME))+"\",\"terminalCompany\":\""+JsonEscape(TerminalInfoString(TERMINAL_COMPANY))+"\",\"terminalBuild\":"+(string)TerminalInfoInteger(TERMINAL_BUILD)+",\"accountServer\":\""+JsonEscape(AccountInfoString(ACCOUNT_SERVER))+"\",\"accountCurrency\":\""+JsonEscape(AccountInfoString(ACCOUNT_CURRENCY))+"\",\"accountMode\":\""+AccountModeName()+"\"}";
   return SendEnvelope("Hello","Hello","null",payload);
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
   const string json="{\"protocolVersion\":1,\"kind\":\""+kind+"\",\"operation\":\""+operation+"\",\"requestId\":"+request_id+",\"sentAtUtc\":\""+UtcNow()+"\",\"payload\":"+payload+"}";
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
