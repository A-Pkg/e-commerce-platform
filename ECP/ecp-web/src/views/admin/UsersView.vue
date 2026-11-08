<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  getAdminUsers,
  getAdminUserDetail,
  disableAdminUser,
  type AdminUserDisableResult,
  AdminUserUnauthorizedError,
  AdminUserForbiddenError,
  type AdminUserFilters,
  type AdminUserResponse,
  type AdminUserRole,
} from '@/api/adminUsers'
import UserDetailDialog from '@/components/admin/users/UserDetailDialog.vue'
import { useAuthStore } from '@/stores/auth'
import type { PagedResponse } from '@/types/pagination'
import { openOwnedMessageBox, type OwnedMessageBox } from '@/utils/ownedMessageBox'

const router = useRouter()
const authStore = useAuthStore()

// 草稿在点击查询或按回车后生效，翻页和刷新沿用已提交条件
const filters = reactive<AdminUserFilters>({ username: '', role: '' })
const appliedFilters = reactive<AdminUserFilters>({ username: '', role: '' })
const hasActiveFilters = computed<boolean>(
  () => appliedFilters.username !== '' || appliedFilters.role !== '',
)

// 列表和分页信息均使用后端返回值
const users = ref<AdminUserResponse[]>([])
const pageNumber = ref<number>(1)
const pageSize = ref<number>(20)
const totalCount = ref<number>(0)
const totalPages = ref<number>(1)
const isLoading = ref<boolean>(false)
const loadError = ref<string>('')

// 详情接口查询成功后，再打开现有公共弹窗
const loadingDetailId = ref<number | null>(null)
const detailError = ref<string>('')
const isDetailDialogVisible = ref<boolean>(false)
const selectedUser = ref<AdminUserResponse | null>(null)
const dialogSessionKey = ref<number>(0)

// 停用确认和请求期间，锁住列表、翻页及其他操作
const disableAction = ref<'confirm' | 'request' | null>(null)
const disablingUserId = ref<number | null>(null)
const disableMessage = ref<string>('')
const disableError = ref<boolean>(false)
let disableMessageBox: OwnedMessageBox | null = null

// 列表、详情、停用互斥，弹窗实际关闭后才允许下一次操作
const isBusy = computed<boolean>(
  () =>
    isLoading.value ||
    loadingDetailId.value !== null ||
    selectedUser.value !== null ||
    disableAction.value !== null,
)

const roleLabels: Record<AdminUserRole, string> = {
  User: '普通用户',
  Admin: '管理员',
}

let isPageActive: boolean = true
let requestVersion: number = 0

// 列表和详情只接收当前页面、当前请求及原管理员会话的结果
function isCurrentRequest(version: number, accessToken: string): boolean {
  return (
    isPageActive && version === requestVersion &&
    authStore.isAdmin && authStore.isCurrentSession(accessToken)
  )
}

// 列表和详情共用权限错误处理
function handleAccessError(error: unknown): boolean {
  if (error instanceof AdminUserUnauthorizedError) {
    authStore.clearSession(true)
    authStore.sessionExpired = true
    return true
  }

  if (error instanceof AdminUserForbiddenError) {
    users.value = []
    pageNumber.value = 1
    pageSize.value = 20
    totalCount.value = 0
    totalPages.value = 1
    isDetailDialogVisible.value = false
    selectedUser.value = null
    detailError.value = ''
    disableMessage.value = ''
    disableError.value = false
    loadError.value = error.message
    return true
  }

  return false
}

// 只关闭本页面持有的停用确认框
function closeDisableConfirmation(): void {
  const box: OwnedMessageBox | null = disableMessageBox
  disableMessageBox = null
  box?.close()
}

// 停用普通用户，成功或冲突后按原筛选条件和页码刷新
async function handleDisableUser(id: number): Promise<void> {
  const user: AdminUserResponse | undefined = users.value.find(item => item.id === id)

  if (
    !isPageActive || !authStore.isAdmin || isBusy.value ||
    !user || user.role !== 'User' || !user.isActive
  ) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion
  const filterSnapshot: AdminUserFilters = { ...appliedFilters }
  const requestedPage: number = pageNumber.value
  const trigger: HTMLElement | null =
    document.activeElement instanceof HTMLElement ? document.activeElement : null
  let box: OwnedMessageBox | null = null
  let result: AdminUserDisableResult | null = null

  disablingUserId.value = id
  disableAction.value = 'confirm'
  disableMessage.value = ''
  disableError.value = false
  detailError.value = ''

  try {
    box = openOwnedMessageBox({
      title: '停用用户',
      message: '确定停用账号“' + user.username + '”吗？停用后，该用户将无法登录或继续使用需要登录的功能。',
      type: 'warning',
      showCancelButton: true,
      confirmButtonText: '确认停用',
      cancelButtonText: '暂不停用',
    })
    disableMessageBox = box
    const confirmed: boolean = await box.result

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    disableMessageBox = null
    const currentUser: AdminUserResponse | undefined = users.value.find(item => item.id === id)
    if (!confirmed || !currentUser || currentUser.role !== 'User' || !currentUser.isActive) {
      return
    }

    disableAction.value = 'request'
    result = await disableAdminUser(id, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    disableMessage.value = '账号“' + user.username + '”：' + result.message
    disableError.value = result.outcome === 'conflict'
    if (result.outcome === 'disabled') {
      currentUser.isActive = false
    }

    const refreshedPage: PagedResponse<AdminUserResponse> =
      await getAdminUsers(accessToken, filterSnapshot, requestedPage)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    users.value = refreshedPage.items
    pageNumber.value = refreshedPage.pageNumber
    pageSize.value = refreshedPage.pageSize
    totalCount.value = refreshedPage.totalCount
    totalPages.value = refreshedPage.totalPages
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken) || handleAccessError(error)) {
      return
    }

    const message: string =
      error instanceof Error ? error.message : '暂时无法确认停用结果，请刷新用户列表后确认'
    disableError.value = true
    disableMessage.value = result === null
      ? message
      : '账号“' + user.username + '”：' + result.message + '；用户列表刷新失败：' + message
  } finally {
    if (box !== null && disableMessageBox === box) {
      closeDisableConfirmation()
    }

    if (isCurrentRequest(version, accessToken)) {
      disableAction.value = null
      disablingUserId.value = null
      await nextTick()
      if (
        isCurrentRequest(version, accessToken) &&
        trigger?.isConnected && !trigger.matches(':disabled')
      ) {
        trigger.focus({ preventScroll: true })
      }
    }
  }
}

// 查询当前页；失败后重新加载时保留请求页码和筛选条件
async function loadUsers(targetPage: number = pageNumber.value): Promise<void> {
  if (!isPageActive || !authStore.isAdmin || isBusy.value) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = ++requestVersion
  const filterSnapshot: AdminUserFilters = { ...appliedFilters }
  pageNumber.value = targetPage
  isLoading.value = true
  loadError.value = ''
  detailError.value = ''
  disableMessage.value = ''
  disableError.value = false
  users.value = []

  try {
    const result: PagedResponse<AdminUserResponse> =
      await getAdminUsers(accessToken, filterSnapshot, targetPage)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    users.value = result.items
    pageNumber.value = result.pageNumber
    pageSize.value = result.pageSize
    totalCount.value = result.totalCount
    totalPages.value = result.totalPages
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken) || handleAccessError(error)) {
      return
    }

    loadError.value = error instanceof Error ? error.message : '获取后台用户列表失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      isLoading.value = false
    }
  }
}

// 新的筛选条件从第一页开始查询
function handleSearch(): void {
  if (!isPageActive || !authStore.isAdmin || isBusy.value) {
    return
  }

  appliedFilters.username = filters.username.trim()
  appliedFilters.role = filters.role
  totalCount.value = 0
  totalPages.value = 1
  void loadUsers(1)
}

// 翻页保留已经提交的筛选条件
function handleChangePage(targetPage: number): void {
  if (
    !isPageActive || !authStore.isAdmin || isBusy.value || loadError.value !== '' ||
    targetPage < 1 || targetPage > totalPages.value || targetPage === pageNumber.value
  ) {
    return
  }

  void loadUsers(targetPage)
}

// 点击详情后单独请求最新信息，不直接使用列表记录打开弹窗
async function openDetailDialog(id: number): Promise<void> {
  if (!isPageActive || !authStore.isAdmin || isBusy.value) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion
  loadingDetailId.value = id
  detailError.value = ''

  try {
    const result: AdminUserResponse = await getAdminUserDetail(id, accessToken)

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    selectedUser.value = result
    isDetailDialogVisible.value = true
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken) || handleAccessError(error)) {
      return
    }

    detailError.value = error instanceof Error ? error.message : '获取后台用户详情失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      loadingDetailId.value = null
    }
  }
}

// 等实际关闭动画结束后清理详情内容
function handleDetailDialogClosed(): void {
  if (!isDetailDialogVisible.value) {
    selectedUser.value = null
  }
}

// 将 UTC 时间转换为浏览器所在时区
function formatDate(value: string): string {
  return new Date(value).toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  })
}

// 首次进入时加载，账号或角色变化时立即清除旧数据和查询状态
watch(
  () => [authStore.sessionId, authStore.isAuthenticated, authStore.isAdmin] as const,
  () => {
    requestVersion += 1
    closeDisableConfirmation()
    disableAction.value = null
    disablingUserId.value = null
    disableMessage.value = ''
    disableError.value = false
    filters.username = ''
    filters.role = ''
    appliedFilters.username = ''
    appliedFilters.role = ''
    users.value = []
    pageNumber.value = 1
    pageSize.value = 20
    totalCount.value = 0
    totalPages.value = 1
    isLoading.value = false
    loadError.value = ''
    loadingDetailId.value = null
    detailError.value = ''
    isDetailDialogVisible.value = false
    selectedUser.value = null
    dialogSessionKey.value += 1

    if (isPageActive && authStore.isAdmin) {
      void loadUsers(1)
    }
  },
  { immediate: true, flush: 'sync' },
)

// 会话更新结束后处理退出或管理员身份变化
watch(
  () => [authStore.isAuthenticated, authStore.isAdmin] as const,
  ([authenticated, isAdmin]) => {
    if (!isPageActive) {
      return
    }

    if (!authenticated) {
      void router.replace({ name: 'login' })
    } else if (!isAdmin) {
      void router.replace({ name: 'home' })
    }
  },
  { flush: 'post' },
)

// 离页后使请求失效，并关闭当前业务弹窗
onBeforeUnmount(() => {
  isPageActive = false
  requestVersion += 1
  closeDisableConfirmation()
  isDetailDialogVisible.value = false
  selectedUser.value = null
})
</script>

<template>
  <div class="users-page">
    <header class="page-heading">
      <h1>用户管理</h1>
      <p>查看用户账号、角色、账号状态和注册时间</p>
    </header>

    <section
      class="table-panel"
      aria-labelledby="user-list-title"
      :aria-busy="isBusy"
    >
      <form class="filter-form" novalidate @submit.prevent="handleSearch">
        <div class="filter-field username-filter">
          <label for="admin-user-username">用户账号</label>
          <input
            id="admin-user-username"
            v-model="filters.username"
            type="search"
            maxlength="50"
            placeholder="请输入用户账号"
            :disabled="isBusy"
          />
        </div>

        <div class="filter-field">
          <label for="admin-user-role">用户角色</label>
          <select id="admin-user-role" v-model="filters.role" :disabled="isBusy">
            <option value="">全部角色</option>
            <option v-for="(label, role) in roleLabels" :key="role" :value="role">
              {{ label }}
            </option>
          </select>
        </div>

        <div class="filter-actions">
          <button class="button primary-button" type="submit" :disabled="isBusy">查询</button>
        </div>
      </form>

      <p v-if="detailError" class="notice-message error-message" role="alert">{{ detailError }}</p>

      <p
        v-if="disableMessage"
        class="notice-message"
        :class="{ 'error-message': disableError }"
        :role="disableError ? 'alert' : 'status'"
      >
        {{ disableMessage }}
      </p>


      <div class="table-heading">
        <h2 id="user-list-title">用户列表</h2>
        <button class="button" type="button" :disabled="isBusy" @click="loadUsers()">
          刷新列表
        </button>
      </div>

      <div class="table-wrapper" role="region" aria-label="用户列表，可横向滚动" tabindex="0">
        <table class="user-table">
          <thead>
            <tr>
              <th scope="col">用户账号</th>
              <th scope="col">角色</th>
              <th scope="col">账号状态</th>
              <th scope="col">注册时间</th>
              <th scope="col">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="isLoading">
              <td colspan="5" class="empty-cell" role="status">正在加载用户…</td>
            </tr>
            <tr v-else-if="loadError">
              <td colspan="5" class="empty-cell">
                <p class="load-error-message" role="alert">{{ loadError }}</p>
                <button class="button" type="button" :disabled="isBusy" @click="loadUsers()">
                  重新加载
                </button>
              </td>
            </tr>
            <tr v-else-if="users.length === 0">
              <td colspan="5" class="empty-cell">
                {{ hasActiveFilters ? '没有符合条件的用户' : '暂无用户' }}
              </td>
            </tr>
            <template v-else>
              <tr v-for="user in users" :key="user.id">
                <td>
                  <div class="username">{{ user.username }}</div>
                  <div class="user-id">编号：{{ user.id }}</div>
                </td>
                <td>
                  <span class="role-badge" :class="{ 'is-admin': user.role === 'Admin' }">
                    {{ roleLabels[user.role] }}
                  </span>
                </td>
                <td>
                  <span class="account-status" :class="{ 'is-disabled': !user.isActive }">
                    {{ user.isActive ? '启用' : '已停用' }}
                  </span>
                </td>
                <td class="date-cell">{{ formatDate(user.createdAtUtc) }}</td>
                <td>
                  <div class="row-actions">
                    <button
                      class="text-button"
                      type="button"
                      :disabled="isBusy"
                      @click="openDetailDialog(user.id)"
                    >
                      {{ loadingDetailId === user.id ? '正在查询…' : '查看详情' }}
                    </button>
                    <button
                      v-if="user.role === 'User' && user.isActive"
                      class="text-button"
                      type="button"
                      :disabled="isBusy"
                      @click="handleDisableUser(user.id)"
                    >
                      {{ disablingUserId === user.id && disableAction === 'request' ? '停用中…' : '停用' }}
                    </button>
                  </div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>

      <footer class="pagination">
        <span v-if="!isLoading && !loadError">共 {{ totalCount }} 条，每页 {{ pageSize }} 条</span>
        <span v-else>{{ isLoading ? '正在读取用户' : '用户加载失败' }}</span>
        <div class="pagination-actions">
          <button
            class="button page-button"
            type="button"
            :disabled="isBusy || loadError !== '' || pageNumber <= 1"
            @click="handleChangePage(pageNumber - 1)"
          >
            上一页
          </button>
          <span class="page-number" aria-live="polite">第 {{ pageNumber }} / {{ totalPages }} 页</span>
          <button
            class="button page-button"
            type="button"
            :disabled="isBusy || loadError !== '' || pageNumber >= totalPages"
            @click="handleChangePage(pageNumber + 1)"
          >
            下一页
          </button>
        </div>
      </footer>
    </section>

    <UserDetailDialog
      :key="dialogSessionKey"
      v-model="isDetailDialogVisible"
      :user="selectedUser"
      @closed="handleDetailDialogClosed"
    />
  </div>
</template>

<style scoped>
/* 页面允许在后台内容容器中缩小 */
.users-page {
  min-width: 0;
}

/* 页面标题 */
.page-heading {
  margin-bottom: 24px;
}

.page-heading h1 {
  margin: 0 0 5px;
  font-size: 25px;
  font-weight: 600;
  letter-spacing: -0.6px;
}

.page-heading p {
  margin: 0;
  color: #919eb2;
  font-size: 12px;
}

/* 页面内按钮的公共样式 */
.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 37px;
  padding: 8px 15px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #65738b;
  font-size: 12px;
  white-space: nowrap;
}

.button:hover:not(:disabled) {
  border-color: #c9d9f5;
  background: #f6f9ff;
}

.primary-button {
  border-color: #2864e8;
  background: #2864e8;
  color: #ffffff;
}

.primary-button:hover:not(:disabled) {
  border-color: #1e55d0;
  background: #1e55d0;
}

/* 筛选和表格放在白色卡片中 */
.table-panel {
  min-width: 0;
  overflow: hidden;
  border: 1px solid #e6ebf3;
  border-radius: 12px;
  background: #ffffff;
}

/* 筛选区域 */
.filter-form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 14px;
  padding: 22px 23px;
  border-bottom: 1px solid #eef1f6;
}

.filter-field {
  min-width: 140px;
  display: flex;
  flex-direction: column;
  gap: 7px;
}

.username-filter {
  min-width: 200px;
}

.filter-field label {
  color: #7f8ba0;
  font-size: 12px;
}

.filter-field input,
.filter-field select {
  width: 100%;
  min-width: 0;
  min-height: 37px;
  padding: 8px 11px;
  border: 1px solid #e0e6f0;
  border-radius: 6px;
  background: #ffffff;
  color: #52617a;
  font-size: 12px;
}

.filter-field input::placeholder {
  color: #b0b9c8;
}

.filter-field input:focus,
.filter-field select:focus {
  outline: none;
  border-color: #83a9fa;
  box-shadow: 0 0 0 3px rgba(40, 100, 232, 0.08);
}

.filter-actions {
  display: flex;
  gap: 9px;
}

/* 详情查询失败提示 */
.notice-message {
  margin: 16px 23px 0;
  padding: 10px 14px;
  border: 1px solid #e4edff;
  border-radius: 7px;
  background: #f6f9ff;
  color: #6f8bb9;
  font-size: 12px;
}

/* 表格标题和刷新按钮 */
.table-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 19px 23px 15px;
}

.table-heading h2 {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
}

/* 表格内部横向滚动 */
.table-wrapper {
  width: 100%;
  overflow-x: auto;
}

.table-wrapper:focus-visible {
  outline: 3px solid #93b6ff;
  outline-offset: -3px;
}

.user-table {
  width: 100%;
  min-width: 740px;
  border-collapse: collapse;
  text-align: left;
}

.user-table th {
  padding: 12px 16px;
  border-top: 1px solid #f0f3f8;
  border-bottom: 1px solid #edf1f7;
  background: #f8fafe;
  color: #8c99ad;
  font-size: 12px;
  font-weight: 400;
  white-space: nowrap;
}

.user-table td {
  padding: 18px 16px;
  border-bottom: 1px solid #f0f3f7;
  color: #67768e;
  font-size: 12px;
}

.user-table th:first-child,
.user-table td:first-child {
  padding-left: 23px;
}

.user-table th:last-child,
.user-table td:last-child {
  padding-right: 23px;
}

.user-table tbody tr:last-child td {
  border-bottom: none;
}

/* 较长的账号允许换行 */
.username {
  min-width: 120px;
  max-width: 220px;
  color: #3c4b63;
  overflow-wrap: anywhere;
}

.user-id {
  margin-top: 4px;
  color: #a8b2c2;
  font-size: 11px;
}

.date-cell {
  white-space: nowrap;
}

/* 用户角色 */
.role-badge {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 5px;
  background: #f0f3f8;
  color: #8593a9;
  font-size: 11px;
  white-space: nowrap;
}

.role-badge.is-admin {
  background: #eef4ff;
  color: #6f91d1;
}

/* 账号状态延续现有蓝灰配色 */
.account-status {
  color: #6f91d1;
  white-space: nowrap;
}

.account-status.is-disabled {
  color: #8593a9;
}

/* 行操作 */
.row-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  white-space: nowrap;
}

.text-button {
  padding: 2px 0;
  border: none;
  background: transparent;
  color: #648dd6;
  font-size: 12px;
}

.text-button:hover:not(:disabled) {
  color: #2864e8;
}

.button:disabled,
.text-button:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.error-message {
  border-color: #f1d7d7;
  background: #fff7f7;
  color: #bd4848;
}

.user-table .load-error-message {
  margin: 0 0 12px;
  color: #bd4848;
}

/* 空列表提示 */
.user-table .empty-cell {
  padding: 64px 20px;
  color: #a1adc0;
  text-align: center;
}

/* 分页区域 */
.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 18px 23px;
  border-top: 1px solid #edf1f7;
  color: #9ba8ba;
  font-size: 12px;
}

.pagination-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.page-button {
  min-height: 29px;
  padding: 4px 9px;
}

.page-button:disabled {
  opacity: 0.45;
}

.page-number {
  min-width: 29px;
  height: 29px;
  padding: 0 8px;
  display: grid;
  place-items: center;
  border-radius: 5px;
  background: #2864e8;
  color: #ffffff;
}

/* 手机上筛选条件上下排列 */
@media (max-width: 700px) {
  .page-heading h1 {
    font-size: 23px;
  }

  .filter-form {
    padding: 18px 16px;
    gap: 12px;
  }

  .filter-field,
  .username-filter {
    width: 100%;
    min-width: 0;
  }

  .filter-actions {
    width: 100%;
  }

  .filter-actions .button {
    flex: 1;
  }

  .notice-message {
    margin-right: 16px;
    margin-left: 16px;
  }

  .table-heading,
  .pagination {
    padding-right: 16px;
    padding-left: 16px;
  }
}
</style>
