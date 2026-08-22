import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom'
import { useAuth } from './auth'
import { AppShell } from './components/AppShell'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import { SettingsPage } from './pages/SettingsPage'
import { SymbolsPage } from './pages/SymbolsPage'
import { BacktestsPage } from './pages/BacktestsPage'
import { PaperTradingPage } from './pages/PaperTradingPage'
import { TradesPage } from './pages/TradesPage'
import { OptimizerPage } from './pages/OptimizerPage'
import { DemoTradingPage } from './pages/DemoTradingPage'

function ProtectedLayout() {
  const { user, isLoading } = useAuth()
  const location = useLocation()

  if (isLoading) return <div className="grid min-h-screen place-items-center text-sm text-slate-500">Loading EMA Bot…</div>
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />

  return <AppShell><Outlet /></AppShell>
}

function LoginRoute() {
  const { user, isLoading } = useAuth()
  if (isLoading) return <div className="grid min-h-screen place-items-center text-sm text-slate-500">Loading EMA Bot…</div>
  return user ? <Navigate to="/" replace /> : <LoginPage />
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginRoute />} />
      <Route element={<ProtectedLayout />}>
        <Route index element={<DashboardPage />} />
        <Route path="backtests" element={<BacktestsPage />} />
        <Route path="paper-trading" element={<PaperTradingPage />} />
        <Route path="demo-trading" element={<DemoTradingPage />} />
        <Route path="trades" element={<TradesPage />} />
        <Route path="symbols" element={<SymbolsPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="optimizer" element={<OptimizerPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
