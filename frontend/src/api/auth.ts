import { http } from './http'

export interface UserDto {
  id: string
  username: string
  email: string
  avatar?: string | null
  isAdmin: boolean
  createdAt: string
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
  user: UserDto
}

export interface RegisterPayload {
  username: string
  email: string
  password: string
}

export interface LoginPayload {
  account: string
  password: string
}

export async function register(payload: RegisterPayload): Promise<AuthResponse> {
  return (await http.post('/auth/register', payload)) as unknown as AuthResponse
}

export async function login(payload: LoginPayload): Promise<AuthResponse> {
  return (await http.post('/auth/login', payload)) as unknown as AuthResponse
}

export async function refresh(token: string): Promise<AuthResponse> {
  return (await http.post('/auth/refresh', { refreshToken: token })) as unknown as AuthResponse
}

export async function logout(token: string): Promise<void> {
  await http.post('/auth/logout', { refreshToken: token })
}

export async function fetchMe(): Promise<UserDto> {
  return (await http.get('/auth/me')) as unknown as UserDto
}
