import { ElMessageBox, type ElMessageBoxOptions } from 'element-plus'
import { h, nextTick, type VNode } from 'vue'

// 每次调用只持有自己的关闭句柄，避免关闭其他业务的确认框
export interface OwnedMessageBox {
  result: Promise<boolean>
  close: () => void
}

// 创建中文确认或提醒框；取消与关闭正常返回 false
export function openOwnedMessageBox(options: ElMessageBoxOptions): OwnedMessageBox {
  const returnFocus: HTMLElement | null =
    document.activeElement instanceof HTMLElement ? document.activeElement : null
  let closeCurrent: (() => void) | null = null
  let forceClosing: boolean = false
  let settled: boolean = false
  const originalMessage = options.message

  const configured: ElMessageBoxOptions = {
    ...options,
    title: options.title ?? '提示',
    customClass: `ecp-message-box ${options.customClass ?? ''}`,
    cancelButtonClass: 'ecp-message-cancel',
    confirmButtonClass: 'ecp-message-confirm',
    confirmButtonText: options.confirmButtonText ?? '确定',
    cancelButtonText: options.cancelButtonText ?? '取消',
    closeOnClickModal: false,
    closeOnPressEscape: true,
    closeOnHashChange: false,
    autofocus: false,
    message: (actions): VNode => {
      closeCurrent = actions.close
      if (forceClosing) {
        queueMicrotask(actions.close)
      }

      return h('div', {
        class: 'ecp-message-content',
        onVnodeMounted: (node) => {
          // 等焦点陷阱挂载后，确认框聚焦取消，提醒框聚焦“知道了”
          void nextTick(() => {
            // Element 的焦点陷阱会在后续 nextTick 初始化，再等一帧设置按钮焦点
            requestAnimationFrame(() => {
              if (forceClosing || settled || !(node.el instanceof HTMLElement)
                || !node.el.isConnected) {
                return
              }
              const selector: string = options.showCancelButton
                ? '.ecp-message-cancel'
                : '.ecp-message-confirm'
              node.el.closest('.el-message-box')
                ?.querySelector<HTMLButtonElement>(selector)?.focus({ preventScroll: true })
            })
          })
        },
      }, [typeof originalMessage === 'function'
        ? originalMessage(actions)
        : typeof originalMessage === 'string'
          ? h('p', originalMessage)
          : originalMessage])
    },
    beforeClose: (action, instance, done) => {
      if (forceClosing || !options.beforeClose) {
        done()
      } else {
        options.beforeClose(action, instance, done)
      }
    },
  }

  const pending = options.showCancelButton
    ? ElMessageBox.confirm(configured.message!, String(configured.title), configured)
    : ElMessageBox.alert(configured.message!, String(configured.title), configured)

  function restoreFocus(): void {
    settled = true
    void nextTick(() => {
      if (!forceClosing && returnFocus?.isConnected) {
        returnFocus.focus({ preventScroll: true })
      }
    })
  }

  const result: Promise<boolean> = pending.then(
    () => {
      restoreFocus()
      return !forceClosing
    },
    (error: unknown) => {
      restoreFocus()
      if (forceClosing || error === 'cancel' || error === 'close') {
        return false
      }
      throw error
    },
  )

  return {
    result,
    close: () => {
      forceClosing = true
      if (!settled) {
        closeCurrent?.()
      }
    },
  }
}
