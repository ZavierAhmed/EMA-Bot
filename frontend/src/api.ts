export type CurrentUser = {
  userName: string
  email: string
  role: string
}

export type HealthStatus = {
  api: string
  database: string
}

type AntiforgeryResponse = { token: string }
type ApiMessage = { message?: string }

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { credentials: 'include', ...init })
  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as ApiMessage
    throw new ApiError(response.status, body.message ?? 'The request could not be completed.')
  }
  return response.status === 204 ? (undefined as T) : (response.json() as Promise<T>)
}

export class ApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message)
  }
}

async function antiforgeryToken(): Promise<string> {
  const response = await request<AntiforgeryResponse>('/api/auth/antiforgery')
  return response.token
}

export async function getCurrentUser(): Promise<CurrentUser> {
  return request<CurrentUser>('/api/auth/me')
}

export async function login(userName: string, password: string): Promise<void> {
  const token = await antiforgeryToken()
  await request<void>('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
    body: JSON.stringify({ userName, password }),
  })
}

export async function logout(): Promise<void> {
  const token = await antiforgeryToken()
  await request<void>('/api/auth/logout', {
    method: 'POST',
    headers: { 'X-CSRF-TOKEN': token },
  })
}

export async function getHealth(): Promise<HealthStatus> {
  return request<HealthStatus>('/api/health')
}
