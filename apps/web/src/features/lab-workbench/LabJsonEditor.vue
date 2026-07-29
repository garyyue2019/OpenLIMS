<script setup lang="ts">
defineProps<{
  label: string
  modelValue: string
  disabled?: boolean
  help?: string
}>()

defineEmits<{ 'update:modelValue': [value: string] }>()
</script>

<template>
  <label class="lab-json-field">
    <span>{{ label }}</span>
    <small v-if="help">{{ help }}</small>
    <textarea
      class="lab-json-editor"
      :value="modelValue"
      :disabled="disabled"
      rows="16"
      spellcheck="false"
      @input="$emit('update:modelValue', ($event.target as HTMLTextAreaElement).value)"
    />
  </label>
</template>
