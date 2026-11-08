<script setup lang="ts">
import { computed } from 'vue'
import AdminDialog from '@/components/admin/AdminDialog.vue'
import type { AdminUserResponse, AdminUserRole } from '@/api/adminUsers'

// 接收显示状态和用户详情
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    user?: AdminUserResponse | null
  }>(),
  {
    user: null,
  },
)

// 将显示状态和关闭事件传给父组件
const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  closed: []
}>()

// 转发 v-model，让共用弹窗与父组件保持同步
const dialogVisible = computed<boolean>({
  get: () => props.modelValue,
  set: (value: boolean) => {
    emit('update:modelValue', value)
  },
})

// 配置角色对应的中文文字
const roleLabels: Record<AdminUserRole, string> = {
  User: '普通用户',
  Admin: '管理员',
}

// 获取角色的中文文字
function getRoleText(role: AdminUserRole): string {
  return roleLabels[role]
}

// 将 UTC 时间转换为浏览器所在时区的显示时间
function formatDate(value: string | null): string {
  if (!value) {
    return '—'
  }

  const date: Date = new Date(value)

  if (Number.isNaN(date.getTime())) {
    return '—'
  }

  return date.toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  })
}

// 通知父组件弹窗已经关闭，供其清除当前用户
function handleClosed(): void {
  emit('closed')
}
</script>

<template>
  <AdminDialog v-model="dialogVisible" title="用户详情" close-text="关闭" @closed="handleClosed">
    <!-- 没有用户数据时，不展示虚构信息 -->
    <p v-if="props.user === null" class="empty-state" role="status">暂无用户详情</p>

    <!-- 用户信息只读展示 -->
    <dl v-else class="detail-grid">
      <div class="detail-field">
        <dt>用户编号</dt>
        <dd>{{ props.user.id }}</dd>
      </div>

      <div class="detail-field">
        <dt>用户账号</dt>
        <dd>{{ props.user.username || '—' }}</dd>
      </div>

      <div class="detail-field">
        <dt>用户角色</dt>
        <dd>
          <span class="role-badge" :class="{ 'is-admin': props.user.role === 'Admin' }">
            {{ getRoleText(props.user.role) }}
          </span>
        </dd>
      </div>

      <div class="detail-field">
        <dt>账号状态</dt>
        <dd>
          <span class="account-status" :class="{ 'is-disabled': !props.user.isActive }">
            {{ props.user.isActive ? '启用' : '已停用' }}
          </span>
        </dd>
      </div>

      <div class="detail-field full-width">
        <dt>注册时间</dt>
        <dd>{{ formatDate(props.user.createdAtUtc) }}</dd>
      </div>
    </dl>
  </AdminDialog>
</template>

<style scoped>
/* 没有用户数据时的提示 */
.empty-state {
  margin: 0;
  padding: 36px 20px;
  color: #9aa7ba;
  font-size: 13px;
  text-align: center;
}

/* 用户信息使用两列展示 */
.detail-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 22px 24px;
  margin: 0;
}

.detail-field {
  min-width: 0;
}

.detail-field dt {
  color: #9aa7ba;
  font-size: 12px;
}

.detail-field dd {
  margin: 6px 0 0;
  color: #52617a;
  font-size: 13px;
  overflow-wrap: anywhere;
}

/* 注册时间占据整行 */
.full-width {
  grid-column: 1 / -1;
}

/* 普通用户角色样式 */
.role-badge {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 5px;
  background: #f0f3f8;
  color: #8593a9;
  font-size: 12px;
}

/* 管理员角色样式 */
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

/* 手机上改为单列 */
@media (max-width: 600px) {
  .detail-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 18px;
  }
}
</style>
