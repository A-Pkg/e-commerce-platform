// 同源标签页共用此锁；回调中的请求和状态更新完成后自动释放
export function withAuthLock<T>(action: () => Promise<T>): Promise<T> {
  if (!navigator.locks) {
    return Promise.reject(new Error('请通过 localhost 或 HTTPS 访问，以支持登录会话同步'))
  }

  return navigator.locks.request('ecp-auth-session', action)
}
