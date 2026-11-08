<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

// 定义后台菜单的数据结构
interface AdminMenuItem {
  title: string
  routeName: string
  iconPath: string
}

// 获取当前路由，用于显示当前页面名称
const route = useRoute()

// 获取路由实例，用于退出后的页面跳转
const router = useRouter()

// 获取现有会话仓库
const authStore = useAuthStore()

// 提取真实账号和登录状态，保留响应式关联
const { currentUser, isAuthenticated } = storeToRefs(authStore)

// 配置后台四个管理模块，后续路由使用相同的名称
const menuItems: AdminMenuItem[] = [
  {
    title: '商品管理',
    routeName: 'admin-products',
    iconPath: 'm12 3 9 5-9 5-9-5 9-5Zm-9 5v10l9 5 9-5V8M12 13v10M7.5 5.5l9 5',
  },
  {
    title: '分类管理',
    routeName: 'admin-categories',
    iconPath: 'M3 3h7v7H3V3Zm11 0h7v7h-7V3ZM3 14h7v7H3v-7Zm11 0h7v7h-7v-7Z',
  },
  {
    title: '订单管理',
    routeName: 'admin-orders',
    iconPath: 'M7 4H5v17h14V4h-2M9 3h6v4H9V3ZM8 12h8m-8 4h5',
  },
  {
    title: '用户管理',
    routeName: 'admin-users',
    iconPath:
      'M12 7a3 3 0 1 1-6 0 3 3 0 0 1 6 0ZM3 21v-3a6 6 0 0 1 12 0v3m2-17a3 3 0 0 1 0 6m4 11v-3a6 6 0 0 0-3-5',
  },
]

// 根据当前路由，显示对应的管理页面名称
const currentPageTitle = computed<string>(() => {
  const currentMenu: AdminMenuItem | undefined = menuItems.find(
    (item: AdminMenuItem) => item.routeName === route.name,
  )

  return currentMenu?.title ?? '管理后台'
})

// 后端确认退出后清理会话，返回共用登录页
async function handleLogout(): Promise<void> {
  if (authStore.isSigningOut) return
  const signedOut: boolean = await authStore.signOut()
  if (signedOut && !authStore.isAuthenticated) await router.replace({ name: 'login' })
}
</script>

<template>
  <div class="admin-layout">
    <!-- 后台公共侧栏 -->
    <aside class="admin-sidebar">
      <RouterLink
        class="admin-brand"
        :to="{ name: 'admin-products' }"
        aria-label="ECP 商城管理后台首页"
      >
        <span class="brand-mark" aria-hidden="true">E</span>

        <span class="brand-text">
          <span class="brand-name">ECP 商城</span>
          <span class="brand-description">管理后台</span>
        </span>
      </RouterLink>

      <p class="menu-caption">业务管理</p>

      <nav class="admin-menu" aria-label="后台管理模块">
        <RouterLink
          v-for="item in menuItems"
          :key="item.routeName"
          class="menu-link"
          active-class="is-active"
          :to="{ name: item.routeName }"
        >
          <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path
              :d="item.iconPath"
              stroke="currentColor"
              stroke-width="1.5"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>

          <span>{{ item.title }}</span>
        </RouterLink>
      </nav>
    </aside>

    <!-- 顶部栏和当前管理页面 -->
    <div class="admin-workspace">
      <header class="admin-topbar">
        <nav class="breadcrumb" aria-label="当前位置">
          <span>管理后台</span>
          <span class="breadcrumb-divider" aria-hidden="true">/</span>
          <strong>{{ currentPageTitle }}</strong>
        </nav>

        <!-- 已登录时显示真实账号和退出按钮 -->
        <div v-if="isAuthenticated && currentUser" class="account-area">
          <span class="account-name">{{ currentUser.username }}</span>

          <button class="logout-button" type="button" :disabled="authStore.isSigningOut" @click="handleLogout">退出登录</button>
        </div>

        <!-- 本阶段允许预览布局，未登录时提供登录入口 -->
        <div v-else class="account-area">
          <RouterLink class="login-link" :to="{ name: 'login' }"> 登录 </RouterLink>
        </div>
      </header>

      <!-- 后台子路由页面在这里显示 -->
      <main class="admin-content">
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
/* 桌面布局：左侧菜单，右侧工作区域 */
.admin-layout {
  min-height: 100vh;
  display: grid;
  grid-template-columns: 232px minmax(0, 1fr);
  background: #f5f7fb;
  color: #28364b;
}

/* 侧栏滚动时停留在视口顶部 */
.admin-sidebar {
  position: sticky;
  top: 0;
  align-self: start;
  min-width: 0;
  min-height: 100vh;
  border-right: 1px solid #e7ecf4;
  background: #ffffff;
}

/* 后台品牌 */
.admin-brand {
  min-height: 84px;
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 18px 26px;
}

.brand-mark {
  width: 38px;
  height: 38px;
  flex-shrink: 0;
  display: grid;
  place-items: center;
  border-radius: 11px;
  background: #2864e8;
  color: #ffffff;
  font-size: 24px;
  font-weight: 700;
}

.brand-text {
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.brand-name {
  font-size: 20px;
  font-weight: 700;
  letter-spacing: -0.5px;
  white-space: nowrap;
}

.brand-description {
  color: #9aa6b8;
  font-size: 11px;
  letter-spacing: 2px;
}

/* 菜单标题 */
.menu-caption {
  margin: 24px 27px 12px;
  color: #9ba7b8;
  font-size: 11px;
}

/* 菜单纵向排列 */
.admin-menu {
  display: flex;
  flex-direction: column;
  gap: 7px;
  padding: 0 15px 24px;
}

.menu-link {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 13px;
  padding: 13px 14px;
  border: 1px solid transparent;
  border-radius: 9px;
  color: #738097;
}

.menu-link:hover {
  background: #f7f9fd;
  color: #2864e8;
}

/* 当前模块高亮 */
.menu-link.is-active {
  border-color: #e4ecff;
  background: #eef4ff;
  color: #2864e8;
  font-weight: 600;
}

.menu-link svg {
  width: 19px;
  height: 19px;
  flex-shrink: 0;
}

.menu-link span {
  overflow-wrap: anywhere;
}

/* 右侧工作区域允许随窗口缩小 */
.admin-workspace {
  min-width: 0;
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

/* 顶部只保留当前位置和账号操作 */
.admin-topbar {
  min-height: 76px;
  display: flex;
  flex-shrink: 0;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 16px 34px;
  border-bottom: 1px solid #e8edf5;
  background: #ffffff;
}

.breadcrumb {
  min-width: 0;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 11px;
  color: #9aa6b7;
  font-size: 12px;
}

.breadcrumb-divider {
  color: #c0c9d6;
}

.breadcrumb strong {
  color: #54627a;
  font-weight: 500;
  overflow-wrap: anywhere;
}

/* 长账号允许换行 */
.account-area {
  min-width: 0;
  max-width: 100%;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  margin-left: auto;
}

.account-name {
  min-width: 0;
  max-width: 180px;
  color: #66738a;
  font-size: 12px;
  overflow-wrap: anywhere;
}

.logout-button,
.login-link {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  padding: 8px 13px;
  border: 1px solid #dfe6ef;
  border-radius: 7px;
  background: #ffffff;
  color: #65738b;
  font-size: 12px;
  white-space: nowrap;
}

.logout-button:hover,
.login-link:hover {
  border-color: #c9d9f5;
  background: #f6f9ff;
  color: #2864e8;
}

/* 各管理页面共用的内容容器 */
.admin-content {
  width: 100%;
  min-width: 0;
  max-width: 1560px;
  flex: 1;
  margin: 0 auto;
  padding: 28px 34px 36px;
}

/* 中等宽度适当缩小侧栏和内边距 */
@media (max-width: 1100px) {
  .admin-layout {
    grid-template-columns: 200px minmax(0, 1fr);
  }

  .admin-brand {
    padding-right: 20px;
    padding-left: 20px;
  }

  .admin-topbar {
    padding-right: 24px;
    padding-left: 24px;
  }

  .admin-content {
    padding: 24px;
  }
}

/* 手机采用上下布局，不增加抽屉菜单逻辑 */
@media (max-width: 700px) {
  .admin-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .admin-sidebar {
    position: static;
    min-height: auto;
    border-right: none;
    border-bottom: 1px solid #e7ecf4;
  }

  .admin-brand {
    min-height: 72px;
    padding: 16px 18px;
  }

  .menu-caption {
    display: none;
  }

  .admin-menu {
    flex-direction: row;
    flex-wrap: wrap;
    gap: 8px;
    padding: 0 18px 16px;
  }

  .menu-link {
    flex: 1 1 130px;
    justify-content: center;
    gap: 8px;
    padding: 10px;
    font-size: 13px;
  }

  .admin-workspace {
    min-height: auto;
  }

  .admin-topbar {
    min-height: 68px;
    padding: 16px 18px;
    gap: 12px;
  }

  .admin-content {
    padding: 24px 18px 32px;
  }
}
</style>
