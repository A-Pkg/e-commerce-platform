// 仅用于前端判断请求归属，不代替后端的 JWT 签名和权限校验
export function readTokenSessionId(accessToken: string): string {
  const payload: string | undefined = accessToken.split('.')[1]
  if (!payload) return ''

  try {
    const claims: { sid?: string } = JSON.parse(
      atob(payload.replace(/-/g, '+').replace(/_/g, '/')),
    )
    return claims.sid ?? ''
  } catch {
    // 无法解析的令牌不能作为当前会话的请求凭据
    return ''
  }
}
