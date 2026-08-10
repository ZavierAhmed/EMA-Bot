import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom'
import { useAuth } from './auth'
import { AppShell } from './components/AppShell'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import { PlaceholderPage } from './pages/PlaceholderPage'

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
        <Route path="backtests" element={<PlaceholderPage title="Backtests" />} />
        <Route path="paper-trading" element={<PlaceholderPage title="Paper Trading" />} />
        <Route path="trades" element={<PlaceholderPage title="Trades" />} />
        <Route path="symbols" element={<PlaceholderPage title="Symbols" />} />
        <Route path="settings" element={<PlaceholderPage title="Settings" />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
