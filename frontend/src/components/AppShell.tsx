import { NavLink, useNavigate } from 'react-router-dom'
import { logout } from '../api'
import { useAuth } from '../auth'

const navigation = [
  ['Dashboard', '/'],
  ['Backtests', '/backtests'],
  ['Paper Trading', '/paper-trading'],
  ['Trades', '/trades'],
  ['Symbols', '/symbols'],
  ['Settings', '/settings'],
]

export function AppShell({ children }: { children: React.ReactNode }) {
  const { user, clearUser } = useAuth()
  const navigate = useNavigate()

  async function handleLogout() {
    try {
      await logout()
    } finally {
      clearUser()
      navigate('/login', { replace: true })
    }
  }

  return (
    <div className="min-h-screen bg-slate-50 text-slate-950 lg:flex">
      <aside className="border-b border-slate-200 bg-white lg:min-h-screen lg:w-64 lg:border-b-0 lg:border-r">
        <div className="flex items-center justify-between px-6 py-5 lg:block">
          <div>
            <p className="text-lg font-semibold tracking-tight">EMA Bot</p>
            <p className="mt-1 text-xs text-slate-500">Private administration</p>
          </div>
          <button onClick={() => void handleLogout()} className="text-sm font-medium text-slate-600 hover:text-slate-950 lg:hidden">Sign out</button>
        </div>
        <nav className="flex gap-1 overflow-x-auto px-3 pb-3 lg:block lg:px-4">
          {navigation.map(([label, href]) => (
            <NavLink key={href} to={href} end={href === '/'} className={({ isActive }) => `block whitespace-nowrap rounded-md px-3 py-2 text-sm ${isActive ? 'bg-slate-100 font-medium text-slate-950' : 'text-slate-600 hover:bg-slate-50 hover:text-slate-950'}`}>
              {label}
            </NavLink>
          ))}
        </nav>
        <div className="hidden border-t border-slate-200 px-6 py-5 lg:block">
          <p className="truncate text-sm font-medium">{user?.userName}</p>
          <button onClick={() => void handleLogout()} className="mt-2 text-sm text-slate-500 hover:text-slate-950">Sign out</button>
        </div>
      </aside>
      <main className="mx-auto w-full max-w-6xl px-6 py-10 lg:px-12">{children}</main>
    </div>
  )
}
