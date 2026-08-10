import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { ApiError, getCurrentUser, type CurrentUser } from './api'

type AuthContextValue = {
  user: CurrentUser | null
  isLoading: boolean
  refresh: () => Promise<void>
  clearUser: () => void
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  const refresh = useCallback(async () => {
    try {
      setUser(await getCurrentUser())
    } catch (error) {
      if (!(error instanceof ApiError) || error.status !== 401) {
        console.error('Could not load the current user.', error)
      }
      setUser(null)
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const value = useMemo(() => ({ user, isLoading, refresh, clearUser: () => setUser(null) }), [user, isLoading, refresh])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// The hook is intentionally colocated with its provider so it can share the private context.
// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used within AuthProvider')
  return context
}
