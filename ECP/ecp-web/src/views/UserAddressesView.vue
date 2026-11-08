<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import {
  createUserAddress,
  getUserAddresses,
  updateUserAddress,
  deleteUserAddress,
  UserAddressUnauthorizedError,
  type CreateUserAddressRequest,
  type UserAddressResponse,
} from '@/api/userAddresses'
import CreateUserAddressDialog from '@/components/addresses/CreateUserAddressDialog.vue'
import StoreHeader from '@/components/layout/StoreHeader.vue'
import { useAuthStore } from '@/stores/auth'
import { openOwnedMessageBox, type OwnedMessageBox } from '@/utils/ownedMessageBox'

const router = useRouter()
const authStore = useAuthStore()

const addresses = ref<UserAddressResponse[]>([])
const isLoading = ref<boolean>(false)
const loadError = ref<string>('')
// 新增和编辑共用同一个弹窗
const isFormOpen = ref<boolean>(false)
const editingAddress = ref<UserAddressResponse | null>(null)
const isSaving = ref<boolean>(false)
const saveError = ref<string>('')

// 打开确认框后即锁定操作，确认后切换为删除状态
const confirmingDeleteId = ref<number | null>(null)
const deletingId = ref<number | null>(null)
const deleteError = ref<string>('')

// 只保存当前地址页面拥有的确认框
let deleteMessageBox: OwnedMessageBox | null = null

// 会话变化时重新创建弹窗，清除上一账号的表单内容
const dialogSessionKey = ref<number>(0)
const isBusy = computed<boolean>(
  () =>
    isLoading.value ||
    isSaving.value ||
    confirmingDeleteId.value !== null ||
    deletingId.value !== null,
)

// 父页面再次保护关闭行为，避免保存期间关闭弹窗
const addressDialogVisible = computed<boolean>({
  get: () => isFormOpen.value,
  set: (visible: boolean) => {
    if (!isSaving.value) {
      isFormOpen.value = visible
    }
  },
})

let isPageActive: boolean = true
let requestVersion: number = 0

// 只有当前页面、当前请求和原登录会话才能处理返回结果
function isCurrentRequest(version: number, accessToken: string): boolean {
  return (
    isPageActive &&
    version === requestVersion &&
    authStore.isAuthenticated &&
    authStore.isCurrentSession(accessToken)
  )
}

// 清理失效会话；跳转由下方登录状态监听处理
function expireSession(): void {
  authStore.clearSession(true)
  authStore.sessionExpired = true
}

// 查询本人地址；区分保存成功后列表刷新失败的情况
async function loadAddresses(addressSaved: boolean = false): Promise<void> {
  if (
    !isPageActive ||
    isBusy.value ||
    isFormOpen.value ||
    !authStore.isAuthenticated
  ) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = ++requestVersion
  isLoading.value = true
  loadError.value = ''
  deleteError.value = ''

  try {
    const result: UserAddressResponse[] = await getUserAddresses(accessToken)
    if (!isCurrentRequest(version, accessToken)) {
      return
    }
    addresses.value = result
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }
    if (error instanceof UserAddressUnauthorizedError) {
      expireSession()
      return
    }

    addresses.value = []
    const message: string = error instanceof Error ? error.message : '获取收货地址失败，请稍后重试'
    loadError.value = addressSaved
      ? `地址已保存，但列表刷新失败，请重新加载。${message}`
      : message
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      isLoading.value = false
    }
  }
}

// 新增时清空编辑对象
function handleOpenCreate(): void {
  if (!isPageActive || isBusy.value || isFormOpen.value || !authStore.isAuthenticated) {
    return
  }
  editingAddress.value = null
  saveError.value = ''
  isFormOpen.value = true
}

// 编辑时记录当前地址，弹窗负责复制内容到表单
function handleOpenEdit(address: UserAddressResponse): void {
  if (!isPageActive || isBusy.value || isFormOpen.value || !authStore.isAuthenticated) {
    return
  }
  editingAddress.value = address
  saveError.value = ''
  isFormOpen.value = true
}

// 根据是否有编辑对象选择新增或修改接口，失败时保留输入
async function handleSave(request: CreateUserAddressRequest): Promise<void> {
  if (!isPageActive || isBusy.value || !isFormOpen.value || !authStore.isAuthenticated) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion
  const addressId: number | null = editingAddress.value?.id ?? null
  isSaving.value = true
  saveError.value = ''

  try {
    if (addressId === null) {
      await createUserAddress(request, accessToken)
    } else {
      await updateUserAddress(addressId, request, accessToken)
    }
    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    // 先解除保存锁并关闭弹窗，再由加载状态锁定操作
    isSaving.value = false
    isFormOpen.value = false
    await loadAddresses(true)
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }
    if (error instanceof UserAddressUnauthorizedError) {
      expireSession()
      return
    }
    saveError.value = error instanceof Error ? error.message : '保存收货地址失败，请稍后重试'
  } finally {
    if (isCurrentRequest(version, accessToken)) {
      isSaving.value = false
    }
  }
}

// 会话变化或离页时，关闭当前页面拥有的确认框
function closeDeleteConfirmation(): void {
  const box: OwnedMessageBox | null = deleteMessageBox
  deleteMessageBox = null
  confirmingDeleteId.value = null
  box?.close()
}

// 确认后删除地址，失败时保留原列表
async function handleDelete(address: UserAddressResponse): Promise<void> {
  if (!isPageActive || isBusy.value || isFormOpen.value || !authStore.isAuthenticated) {
    return
  }

  const accessToken: string = authStore.accessToken
  const version: number = requestVersion
  // 解除操作锁后恢复焦点，避免确认框尝试聚焦仍被禁用的按钮
  const trigger: HTMLElement | null =
    document.activeElement instanceof HTMLElement ? document.activeElement : null
  confirmingDeleteId.value = address.id
  deleteError.value = ''
  let box: OwnedMessageBox | null = null

  try {
    box = openOwnedMessageBox({
      title: '删除收货地址',
      message: '确定删除“' + address.recipientName + '”的这条收货地址吗？',
      type: 'warning',
      showCancelButton: true,
      confirmButtonText: '确认删除',
    })
    deleteMessageBox = box
    const confirmed: boolean = await box.result

    if (!isCurrentRequest(version, accessToken)) {
      return
    }

    deleteMessageBox = null
    confirmingDeleteId.value = null
    if (!confirmed) {
      return
    }

    deletingId.value = address.id
    await deleteUserAddress(address.id, accessToken)
    if (isCurrentRequest(version, accessToken)) {
      addresses.value = addresses.value.filter(current => current.id !== address.id)
    }
  } catch (error: unknown) {
    if (!isCurrentRequest(version, accessToken)) {
      return
    }
    if (error instanceof UserAddressUnauthorizedError) {
      expireSession()
      return
    }
    deleteError.value = error instanceof Error ? error.message : '删除收货地址失败，请稍后重试'
  } finally {
    if (box !== null && deleteMessageBox === box) {
      closeDeleteConfirmation()
    }
    if (isCurrentRequest(version, accessToken)) {
      confirmingDeleteId.value = null
      deletingId.value = null
      await nextTick()
      if (trigger?.isConnected) {
        trigger.focus({ preventScroll: true })
      }
    }
  }
}

// 首次加载；退出或切换账号时立即清理本人数据及请求状态
watch(
  () => [authStore.sessionId, authStore.isAuthenticated] as const,
  () => {
    requestVersion += 1
    closeDeleteConfirmation()
    deletingId.value = null
    deleteError.value = ''
    addresses.value = []
    loadError.value = ''
    saveError.value = ''
    isLoading.value = false
    isSaving.value = false
    isFormOpen.value = false
    editingAddress.value = null
    dialogSessionKey.value += 1
    if (authStore.isAuthenticated) {
      void loadAddresses()
    }
  },
  { immediate: true, flush: 'sync' },
)

// 等会话同步更新结束后再跳转，避免重新登录过程中的临时清理触发跳转
watch(
  () => authStore.isAuthenticated,
  authenticated => {
    if (!authenticated && isPageActive) {
      void router.replace({ name: 'login' })
    }
  },
  { flush: 'post' },
)

// 离页后丢弃旧请求结果，并关闭本页面的确认框
onBeforeUnmount(() => {
  isPageActive = false
  requestVersion += 1
  closeDeleteConfirmation()
})
</script>

<template>
  <div class="addresses-page">
    <StoreHeader />
    <main class="addresses-content">
      <header class="page-heading">
        <h1>收货地址</h1>
        <div class="page-actions">
          <RouterLink class="home-link" :to="{ name: 'home' }">返回商城</RouterLink>
          <button
            class="secondary-button"
            type="button"
            :disabled="isBusy || isFormOpen"
            @click="loadAddresses()"
          >刷新列表</button>
          <button
            class="primary-button"
            type="button"
            :disabled="isBusy || isFormOpen"
            @click="handleOpenCreate"
          >新增地址</button>
        </div>
      </header>

      <p v-if="deleteError" class="error-message action-error" role="alert">
        {{ deleteError }}
      </p>

      <section class="addresses-panel" aria-label="本人收货地址" :aria-busy="isBusy">
        <p v-if="isLoading" class="panel-message" role="status">正在加载收货地址…</p>
        <div v-else-if="loadError" class="message-panel">
          <p class="error-message" role="alert">{{ loadError }}</p>
          <button
            class="secondary-button"
            type="button"
            :disabled="isBusy || isFormOpen"
            @click="loadAddresses()"
          >重新加载</button>
        </div>
        <div v-else-if="addresses.length === 0" class="message-panel">
          <p class="muted-message" role="status">还没有收货地址，点击“新增地址”添加。</p>
        </div>
        <ul v-else class="address-list">
          <li v-for="address in addresses" :key="address.id" class="address-card">
            <div class="recipient-row">
              <strong>{{ address.recipientName }}</strong>
              <span>{{ address.phone }}</span>
            </div>
            <p class="address-text">{{ address.address }}</p>
            <div class="card-actions">
              <button
                class="secondary-button"
                type="button"
                :disabled="isBusy || isFormOpen"
                @click="handleOpenEdit(address)"
              >编辑</button>
              <button
                class="secondary-button delete-button"
                type="button"
                :disabled="isBusy || isFormOpen"
                @click="handleDelete(address)"
              >{{ deletingId === address.id ? '删除中…' : '删除' }}</button>
            </div>
          </li>
        </ul>
      </section>
    </main>

    <CreateUserAddressDialog
      :key="dialogSessionKey"
      v-model="addressDialogVisible"
      :saving="isSaving"
      :error-message="saveError"
      :initial-address="editingAddress"
      @submit="handleSave"
    />
  </div>
</template>

<style scoped>
.addresses-page {
  min-height: 100vh;
  background: #f4f6fa;
  color: #253248;
}
.addresses-content {
  box-sizing: border-box;
  width: 100%;
  max-width: 1240px;
  margin: 0 auto;
  padding: 28px 28px 48px;
}
.page-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 20px;
}
.page-heading h1 {
  margin: 0;
  font-size: 25px;
  font-weight: 600;
}
.page-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
}
.home-link {
  color: #2864e8;
  font-size: 14px;
  text-decoration: none;
}
.primary-button,
.secondary-button {
  padding: 9px 16px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #52617a;
  font: inherit;
  font-size: 14px;
  cursor: pointer;
}
.primary-button {
  border-color: #2864e8;
  background: #2864e8;
  color: #ffffff;
}
.primary-button:disabled,
.secondary-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.addresses-panel {
  overflow: hidden;
  border: 1px solid #e5eaf2;
  border-radius: 12px;
  background: #ffffff;
}
.panel-message,
.message-panel {
  margin: 0;
  padding: 48px 20px;
  text-align: center;
}
.message-panel {
  display: grid;
  justify-items: center;
  gap: 16px;
}
.message-panel p {
  margin: 0;
  line-height: 1.7;
}
.panel-message,
.muted-message {
  color: #8793a5;
}
.error-message {
  color: #bd4848;
}
.address-list {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
  margin: 0;
  padding: 20px;
  list-style: none;
}
.address-card {
  min-width: 0;
  padding: 20px;
  border: 1px solid #e5eaf2;
  border-radius: 9px;
}
.recipient-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 16px;
  font-size: 14px;
  overflow-wrap: anywhere;
}
.recipient-row span {
  color: #66738a;
}
.address-text {
  margin: 14px 0 0;
  color: #52617a;
  font-size: 14px;
  line-height: 1.8;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.card-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 16px;
}
.delete-button {
  color: #bd4848;
}
.action-error {
  margin: 0 0 16px;
  font-size: 14px;
  line-height: 1.7;
}
@media (max-width: 700px) {
  .addresses-content { padding: 24px 18px 36px; }
  .address-list { grid-template-columns: minmax(0, 1fr); padding: 16px; }
}
</style>
