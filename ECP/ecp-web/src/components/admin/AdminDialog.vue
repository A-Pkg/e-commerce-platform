<script setup lang="ts">
import { useId } from 'vue'

// 将调用方的业务类名传给实际弹窗，避免依赖 Teleport 的根节点
defineOptions({ inheritAttrs: false })

// 保留现有业务接口，增加关闭锁和可配置宽度
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title: string
    description?: string
    closeText?: string
    closeDisabled?: boolean
    width?: string | number
  }>(),
  {
    description: '',
    closeText: '取消',
    closeDisabled: false,
    width: '720px',
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  closed: []
}>()

const descriptionId: string = `${useId()}-dialog-description`

// 所有用户关闭操作均由父组件决定，保存期间不提前隐藏或清理
function requestClose(): void {
  if (!props.closeDisabled) {
    emit('update:modelValue', false)
  }
}

// 不调用 Element 的 done；父组件接受关闭后，由 modelValue 驱动关闭
function handleBeforeClose(): void {
  requestClose()
}

// Element 在关闭动画结束时同步状态，忽略已经重新打开的旧通知
function handleModelUpdate(visible: boolean): void {
  if (visible) {
    emit('update:modelValue', true)
  } else if (!props.modelValue) {
    emit('update:modelValue', false)
  }
}

// 仅实际关闭并结束动画后通知业务组件清理输入
function handleClosed(): void {
  if (!props.modelValue) {
    emit('closed')
  }
}
</script>

<template>
  <ElDialog
    v-bind="$attrs"
    class="ecp-dialog admin-dialog"
    :model-value="props.modelValue"
    :title="props.title"
    :width="props.width"
    :before-close="handleBeforeClose"
    :close-on-click-modal="false"
    :close-on-press-escape="!props.closeDisabled"
    :aria-describedby="props.description ? descriptionId : undefined"
    header-class="dialog-header"
    body-class="dialog-body"
    footer-class="dialog-footer"
    append-to-body
    align-center
    @update:model-value="handleModelUpdate"
    @closed="handleClosed"
  >
    <template #header="{ titleId, titleClass }">
      <h2 :id="titleId" :class="[titleClass, 'ecp-dialog-title']">
        {{ props.title }}
      </h2>
      <p v-if="props.description" :id="descriptionId" class="ecp-dialog-description">
        {{ props.description }}
      </p>
    </template>

    <slot />

    <template #footer>
      <ElButton class="cancel-button" :disabled="props.closeDisabled" @click="requestClose">
        {{ props.closeText }}
      </ElButton>
      <slot name="actions" />
    </template>
  </ElDialog>
</template>
