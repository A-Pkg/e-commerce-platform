import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '@/views/HomeView.vue'
import LoginView from '@/views/auth/LoginView.vue'
import AdminLayout from '@/components/layout/AdminLayout.vue'
import { useAuthStore } from '@/stores/auth'

// 创建路由实例，配置商城、登录页和后台管理页面
const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: HomeView,
    },
    {
      path: '/login',
      name: 'login',
      component: LoginView,
    },
    {
      path: '/cart',
      name: 'cart',
      component: () => import('@/views/CartView.vue'),

      // 购物车只要求登录，不要求管理员角色
      meta: {
        requiresAuth: true,
      },
    },
    {
      path: '/orders',
      name: 'my-orders',
      component: () => import('@/views/MyOrdersView.vue'),

      // 本人订单列表要求先登录
      meta: {
        requiresAuth: true,
      },
    },
    {
      path: '/orders/:id',
      name: 'order-detail',
      component: () => import('@/views/OrderDetailView.vue'),

      // 订单详情只允许登录用户访问
      meta: {
        requiresAuth: true,
      },
    },
    {
      // 旧结算地址统一返回购物车
      path: '/checkout',
      name: 'checkout',
      redirect: { name: 'cart' },
    },
    {
      path: '/addresses',
      name: 'user-addresses',
      component: () => import('@/views/UserAddressesView.vue'),

      // 收货地址属于本人页面，要求登录
      meta: {
        requiresAuth: true,
      },
    },
    {
      path: '/account',
      name: 'user-account',
      component: () => import('@/views/UserAccountView.vue'),
      meta: {
        requiresAuth: true,
        requiresUser: true,
      },
    },
    {
      // 后台各页面共用同一个布局
      path: '/admin',
      component: AdminLayout,

      // 标记后台及其所有子页面都需要管理员身份
      meta: {
        requiresAdmin: true,
      },

      children: [
        {
          // 访问 /admin 时，默认进入商品管理
          path: '',
          redirect: { name: 'admin-products' },
        },
        {
          path: 'products',
          name: 'admin-products',
          component: () => import('@/views/admin/ProductsView.vue'),
        },
        {
          path: 'categories',
          name: 'admin-categories',
          component: () => import('@/views/admin/CategoriesView.vue'),
        },
        {
          path: 'orders',
          name: 'admin-orders',
          component: () => import('@/views/admin/OrdersView.vue'),
        },
        {
          path: 'users',
          name: 'admin-users',
          component: () => import('@/views/admin/UsersView.vue'),
        },
        {
          // 后台未知地址返回商品管理
          path: ':pathMatch(.*)*',
          redirect: { name: 'admin-products' },
        },
      ],
    },
    {
      // 其他未知地址返回商城首页
      path: '/:pathMatch(.*)*',
      redirect: { name: 'home' },
    },
  ],
})

// 每次切换页面前，检查登录状态和后台访问权限
router.beforeEach(async (to) => {
  // 在守卫执行时获取会话仓库
  const authStore = useAuthStore()

  // 首次导航先恢复登录，避免 Pinia 暂时为空时误跳转登录页
  await authStore.initialize()

  // 已登录用户访问登录页时，根据角色进入对应首页
  if (to.name === 'login' && authStore.isAuthenticated) {
    return {
      name: authStore.isAdmin ? 'admin-products' : 'home',
      replace: true,
    }
  }

  // 购物车等本人页面要求先登录
  if (to.meta.requiresAuth === true && !authStore.isAuthenticated) {
    return { name: 'login', replace: true }
  }

  // 账号管理仅供普通用户使用
  if (
    to.meta.requiresUser === true &&
    authStore.currentUser?.role !== 'User'
  ) {
    return { name: 'admin-products', replace: true }
  }

  // 后台及其子页面需要检查管理员身份
  if (to.meta.requiresAdmin === true) {
    // 未登录时先进入登录页
    if (!authStore.isAuthenticated) {
      return { name: 'login', replace: true }
    }

    // 普通用户访问后台时返回商城首页
    if (!authStore.isAdmin) {
      return { name: 'home', replace: true }
    }
  }

  // 满足访问条件，允许进入目标页面
  return true
})

export default router
