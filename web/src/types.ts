export type Priority = 'low' | 'medium' | 'high'
export type Status = 'open' | 'in_progress' | 'resolved'
export type Role = 'admin' | 'inspector'

export interface Position {
  x: number
  y: number
  z: number
}

export interface Issue {
  id: string
  title: string
  description: string
  priority: Priority
  status: Status
  position: Position
  reporterId: string
  reporterName: string
  createdAt: string
  updatedAt: string
}

export interface User {
  id: string
  username: string
  displayName: string
  role: Role
  createdAt: string
}
