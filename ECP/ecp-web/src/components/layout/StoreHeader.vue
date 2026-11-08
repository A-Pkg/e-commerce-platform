<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { storeToRefs } from 'pinia'
import { RouterLink, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

// 首页可启用流式宽度，其他页面默认保留原有布局
const props = withDefaults(
  defineProps<{
    fluid?: boolean
  }>(),
  {
    fluid: false,
  },
)

// 定义频道导航的数据结构
interface StoreChannel {
  name: string
  style: 'normal' | 'flash' | 'vip'
}

// 获取路由实例，用于退出登录后的跳转
const router = useRouter()

// 获取统一管理登录状态的仓库
const authStore = useAuthStore()

// 提取账号和登录状态，保留响应式关联
const { currentUser, isAuthenticated } = storeToRefs(authStore)

// 保存搜索框内容，目前不发送搜索请求
const searchKeyword = ref<string>('')

// 保存功能尚未开放时的提示
const noticeMessage = ref<string>('')

// 保存提示计时器，便于替换提示和组件卸载时清理
let noticeTimer: ReturnType<typeof setTimeout> | null = null

// 配置商城首页之外的六个频道
const channels: StoreChannel[] = [
  { name: '秒杀特惠', style: 'flash' },
  { name: '精选新品', style: 'normal' },
  { name: '品牌馆', style: 'normal' },
  { name: '领券中心', style: 'normal' },
  { name: '数码潮流汇', style: 'normal' },
  { name: 'VIP会员专享', style: 'vip' },
]

// 取消上一次提示的计时器
function clearNoticeTimer(): void {
  if (noticeTimer !== null) {
    clearTimeout(noticeTimer)
    noticeTimer = null
  }
}

// 显示提示，三秒后自动清除
function showNotice(message: string): void {
  clearNoticeTimer()
  noticeMessage.value = message

  noticeTimer = setTimeout(() => {
    noticeMessage.value = ''
    noticeTimer = null
  }, 3000)
}

// 提交搜索时显示提示，暂不调用接口
function handleSearch(): void {
  showNotice('搜索功能即将开放')
}

// 点击尚未开放的频道时显示提示
function handleChannelClick(channelName: string): void {
  showNotice(`${channelName}功能即将开放`)
}

// 后端确认退出后清理会话，并通知同一浏览器的其他标签页
async function handleLogout(): Promise<void> {
  if (authStore.isSigningOut) return
  const signedOut: boolean = await authStore.signOut()
  if (signedOut && !authStore.isAuthenticated) await router.replace({ name: 'login' })
}

// 离开页面时取消计时器
onBeforeUnmount(() => {
  clearNoticeTimer()
})
</script>

<template>
  <header class="store-header" :class="{ 'is-fluid': props.fluid }">
    <!-- 第一行：品牌、搜索和账号区域 -->
    <div class="store-container header-content">
      <RouterLink class="brand" :to="{ name: 'home' }" aria-label="ECP 商城首页">
        <span class="brand-mark" aria-hidden="true">E</span>
        <span class="brand-name">ECP<span>商城</span></span>
      </RouterLink>

      <!-- 点击按钮或按回车，均会触发表单提交 -->
      <form class="search-form" role="search" @submit.prevent="handleSearch">
        <svg class="search-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true">
          <circle cx="10.8" cy="10.8" r="6.8" stroke="currentColor" stroke-width="1.7" />
          <path
            d="m16 16 4.4 4.4"
            stroke="currentColor"
            stroke-width="1.7"
            stroke-linecap="round"
          />
        </svg>

        <label class="visually-hidden" for="store-search-input"> 搜索商品名称 </label>

        <input
          id="store-search-input"
          v-model="searchKeyword"
          type="search"
          placeholder="搜索你想要的商品"
          autocomplete="off"
          maxlength="100"
        />

        <button class="primary-button search-button" type="submit">搜索</button>
      </form>

      <!-- 已登录时显示账号、购物车、订单、收货地址、账号管理入口和退出按钮 -->
      <div v-if="isAuthenticated && currentUser" class="account-area">
        <span class="username">{{ currentUser.username }}</span>

        <RouterLink class="primary-button" :to="{ name: 'cart' }">
          购物车
        </RouterLink>

        <RouterLink class="logout-button address-link" :to="{ name: 'my-orders' }">
          我的订单
        </RouterLink>

        <RouterLink class="logout-button address-link" :to="{ name: 'user-addresses' }">
          收货地址
        </RouterLink>

        <RouterLink
          v-if="currentUser.role === 'User'"
          class="logout-button address-link"
          :to="{ name: 'user-account' }"
        >
          账号管理
        </RouterLink>

        <button class="logout-button" type="button" :disabled="authStore.isSigningOut" @click="handleLogout">
          退出登录
        </button>
      </div>

      <!-- 游客通过登录页进行登录或注册 -->
      <div v-else class="account-area">
        <RouterLink class="primary-button" :to="{ name: 'login' }"> 登录 / 注册 </RouterLink>
      </div>
    </div>

    <!-- 第二行：商城频道导航 -->
    <nav class="channel-nav" aria-label="商城频道">
      <div class="store-container channel-content">
        <RouterLink class="channel-link home-link" :to="{ name: 'home' }"> 商城首页 </RouterLink>

        <button
          v-for="channel in channels"
          :key="channel.name"
          class="channel-link"
          :class="{
            'flash-link': channel.style === 'flash',
            'vip-link': channel.style === 'vip',
          }"
          type="button"
          @click="handleChannelClick(channel.name)"
        >
          <svg
            v-if="channel.style === 'flash'"
            class="flash-icon"
            viewBox="0 0 20 24"
            aria-hidden="true"
          >
            <path d="m12 1-9 13h6l-1 9 9-14h-6Z" fill="currentColor" />
          </svg>

          {{ channel.name }}
        </button>
      </div>
    </nav>
  </header>

  <!-- 提示区域保持存在，便于辅助工具读取内容变化 -->
  <div class="notice-region" role="status" aria-live="polite" aria-atomic="true">
    <p v-if="noticeMessage" class="notice-message">
      {{ noticeMessage }}
    </p>
  </div>
</template>

<style scoped>
/* 顶部使用白色背景，与主体背景区分 */
.store-header {
  background: #ffffff;
  border-bottom: 1px solid #e6eaf1;
}

/* 顶部内容与后续首页主体保持相同的最大宽度 */
.store-container {
  width: 100%;
  max-width: 1240px;
  margin: 0 auto;
  padding-right: 28px;
  padding-left: 28px;
}

/* 仅启用流式布局的首页取消顶部宽度上限 */
.store-header.is-fluid .store-container {
  max-width: none;
}

/* 品牌、搜索框和账号区域横向排列 */
.header-content {
  min-height: 92px;
  display: flex;
  align-items: center;
  gap: 32px;
}

/* 商城品牌 */
.brand {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  gap: 11px;
  color: #253248;
  white-space: nowrap;
}

.brand-mark {
  width: 40px;
  height: 40px;
  display: grid;
  place-items: center;
  border-radius: 12px;
  background: #2864e8;
  color: #ffffff;
  font-size: 25px;
  font-weight: 700;
}

.brand-name {
  font-size: 23px;
  font-weight: 700;
  letter-spacing: -0.6px;
}

.brand-name span {
  margin-left: 5px;
  font-size: 19px;
  font-weight: 600;
}

/* 搜索框填充顶部中间区域 */
.search-form {
  min-width: 0;
  max-width: 530px;
  flex: 1;
  display: flex;
  align-items: center;
  margin: 0 auto;
  padding: 5px 5px 5px 15px;
  border: 1px solid #e0e6f0;
  border-radius: 11px;
  background: #f7f9fc;
}

.search-form:focus-within {
  border-color: #7ca5ff;
  box-shadow: 0 0 0 3px rgba(40, 100, 232, 0.08);
}

.search-icon {
  width: 20px;
  height: 20px;
  flex-shrink: 0;
  color: #94a0b5;
}

.search-form input {
  width: 100%;
  min-width: 0;
  padding: 8px 11px;
  border: none;
  outline: none;
  background: transparent;
  color: #253248;
}

.search-form input::placeholder {
  color: #9aa5b6;
}

/* 输入框焦点由外层搜索框统一展示 */
.search-form input:focus-visible {
  outline: none;
}

/* 标签供辅助工具读取，不在页面中占用空间 */
.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  margin: -1px;
  padding: 0;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: none;
}

/* 搜索按钮和登录入口使用同一套主按钮样式 */
.primary-button {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  padding: 9px 18px;
  border: none;
  border-radius: 8px;
  background: #2864e8;
  color: #ffffff;
  white-space: nowrap;
  transition: background 0.16s;
}

.primary-button:hover {
  background: #1b52cd;
}

/* 账号较长时允许换行 */
.account-area {
  min-width: 0;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
}

.username {
  min-width: 0;
  max-width: 160px;
  color: #66738a;
  font-size: 13px;
  overflow-wrap: anywhere;
}

/* 收货地址入口沿用次要按钮外观 */
.address-link {
  text-decoration: none;
}

/* 退出使用次要按钮样式 */
.logout-button {
  flex-shrink: 0;
  padding: 8px 12px;
  border: 1px solid #e1e6ef;
  border-radius: 8px;
  background: #ffffff;
  color: #526077;
  white-space: nowrap;
}

.logout-button:hover {
  background: #f7f9fc;
}

/* 频道导航单独占一行 */
.channel-nav {
  border-top: 1px solid #f1f3f7;
}

.channel-content {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0 30px;
}

.channel-link {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 15px 0;
  border: none;
  background: transparent;
  color: #46536a;
  font-size: 15px;
  white-space: nowrap;
}

.channel-link:hover {
  color: #2864e8;
}

/* 当前首页入口 */
.home-link {
  color: #2864e8;
  font-weight: 700;
}

.home-link::after {
  content: '';
  position: absolute;
  right: 0;
  bottom: 0;
  left: 0;
  height: 3px;
  border-radius: 3px 3px 0 0;
  background: #2864e8;
}

/* 秒杀入口使用红色强调 */
.flash-link {
  color: #f34d47;
  font-weight: 700;
}

.flash-link:hover {
  color: #dc3933;
}

.flash-icon {
  width: 17px;
  height: 20px;
  color: #ff8458;
}

/* 会员入口靠右，并使用橙色强调 */
.vip-link {
  margin-left: auto;
  color: #dd7a00;
  font-weight: 700;
}

.vip-link:hover {
  color: #bd6700;
}

/* 提示显示在窗口底部，不影响页面布局 */
.notice-region {
  position: fixed;
  right: 16px;
  bottom: 24px;
  left: 16px;
  z-index: 50;
  display: flex;
  justify-content: center;
  pointer-events: none;
}

.notice-message {
  max-width: 100%;
  margin: 0;
  padding: 12px 20px;
  border-radius: 10px;
  background: #253248;
  color: #ffffff;
  box-shadow: 0 8px 24px rgba(37, 50, 72, 0.15);
  font-size: 13px;
  text-align: center;
  overflow-wrap: anywhere;
}

/* 中等宽度减少导航间距 */
@media (max-width: 1000px) {
  .header-content {
    gap: 24px;
  }

  .channel-content {
    gap: 0 23px;
  }

  .channel-link {
    font-size: 14px;
  }
}

/* 手机上搜索框另起一行，频道导航自动换行 */
@media (max-width: 700px) {
  .store-container {
    padding-right: 18px;
    padding-left: 18px;
  }

  .header-content {
    flex-wrap: wrap;
    gap: 18px;
    padding-top: 18px;
    padding-bottom: 18px;
  }

  .brand-mark {
    width: 35px;
    height: 35px;
    font-size: 22px;
  }

  .brand-name {
    font-size: 21px;
  }

  .brand-name span {
    font-size: 17px;
  }

  .account-area {
    flex: 1;
    flex-wrap: wrap;
    margin-left: auto;
  }

  .account-area .primary-button,
  .logout-button {
    padding: 8px 12px;
    font-size: 12px;
  }

  .search-form {
    order: 3;
    flex-basis: 100%;
    max-width: none;
  }

  .channel-content {
    gap: 0 20px;
  }

  .channel-link {
    padding: 11px 0;
    font-size: 13px;
  }

  .vip-link {
    margin-left: 0;
  }
}

/* 减少动态效果的系统设置下，关闭按钮过渡 */
@media (prefers-reduced-motion: reduce) {
  .primary-button {
    transition: none;
  }
}
</style>
