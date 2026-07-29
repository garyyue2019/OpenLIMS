<script setup lang="ts">
import { computed } from 'vue'
import { isJsonRecord, prettyJson } from './lab-json'

const props = withDefaults(defineProps<{
  title: string
  value: unknown
  blocked?: boolean
}>(), { blocked: false })

const formatted = computed(() => prettyJson(props.value))
const summary = computed(() => {
  if (!isJsonRecord(props.value)) return []
  const source = props.value
  const candidates = [
    ['对象 ID', firstValue(source, [
      'fileRegistrationId', 'resultGroupId', 'qcRunId', 'reportId',
      'signatureId', 'actionId', 'exceptionId'
    ])],
    ['精确版本', firstValue(source, [
      'version', 'currentVersion', 'currentGroupVersion', 'currentBatchVersion',
      'groupVersion', 'reportVersion', 'versionNumber', 'nextVersionNumber'
    ])],
    ['状态', firstValue(source, ['state', 'chainState'])],
    ['决定', firstValue(source, ['decision', 'verdict'])],
    ['规则集', firstValue(source, ['ruleSetVersion'])]
  ]
  return candidates.filter((entry): entry is [string, string | number] => entry[1] !== undefined)
})
const reasonCodes = computed(() => {
  if (!isJsonRecord(props.value) || !Array.isArray(props.value.reasonCodes)) return []
  return props.value.reasonCodes.filter((value): value is string => typeof value === 'string')
})

function firstValue(source: Record<string, unknown>, keys: string[]): string | number | undefined {
  for (const key of keys) {
    const value = source[key]
    if (typeof value === 'string' || typeof value === 'number') return value
  }
  return undefined
}
</script>

<template>
  <section class="lab-panel" :class="blocked ? 'lab-blocked' : 'lab-result'" aria-live="polite">
    <h2>{{ title }}</h2>
    <dl v-if="summary.length" class="lab-details">
      <div v-for="([label, item], index) in summary" :key="index"><dt>{{ label }}</dt><dd>{{ item }}</dd></div>
    </dl>
    <p v-if="reasonCodes.length"><strong>原因码：</strong>{{ reasonCodes.join('、') }}</p>
    <slot />
    <details>
      <summary>查看完整服务器响应</summary>
      <pre class="lab-json-response">{{ formatted }}</pre>
    </details>
  </section>
</template>
