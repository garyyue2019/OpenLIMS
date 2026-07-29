<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router'
import { authSnapshot, runtimeConfig, signIn } from '../auth-store'

const route = useRoute()
</script>

<template>
  <main class="hero" aria-labelledby="page-title">
    <a-card :bordered="false" class="hero-card">
      <p class="eyebrow">SYSTEM SHELL</p>
      <h1 id="page-title">OpenLIMS</h1>
      <p>通过受保护的工作台执行从收样到受控报告签发的实验室全流程操作。</p>
      <a-alert v-if="authSnapshot.status === 'configuration-error'" type="error" show-icon message="Configuration error" description="Sign-in is unavailable until the protected runtime configuration is completed." role="alert" />
      <a-spin v-else-if="authSnapshot.status === 'loading'" tip="Restoring secure session…" />
      <a-alert v-else-if="authSnapshot.status === 'expired'" type="warning" show-icon message="Session expired" description="Sign in again to continue." role="status">
        <template #action><a-button size="small" @click="signIn(route.fullPath)">Sign in</a-button></template>
      </a-alert>
      <a-result v-else-if="authSnapshot.status === 'anonymous'" status="info" title="Sign in required" sub-title="Use your organization’s configured identity provider to access this technical shell.">
        <template #extra><a-button type="primary" @click="signIn(route.fullPath)">Sign in</a-button></template>
      </a-result>
      <a-descriptions v-else-if="authSnapshot.status === 'authenticated' && runtimeConfig" :column="1" size="small" title="Session restored">
        <a-descriptions-item label="Environment">{{ runtimeConfig.environmentLabel }}</a-descriptions-item>
      </a-descriptions>
      <section v-if="authSnapshot.status === 'authenticated'" class="operator-launchpad" aria-labelledby="operator-launchpad-title">
        <h2 id="operator-launchpad-title">实验室操作入口</h2>
        <div class="operator-grid">
          <RouterLink :to="{ name: 'receiving.registration' }"><strong>到货登记</strong><span>建立收样实物和包装身份</span></RouterLink>
          <RouterLink :to="{ name: 'workbench.scope' }"><strong>范围矩阵</strong><span>批准检测范围并检查生产资格</span></RouterLink>
          <RouterLink :to="{ name: 'workbench.quantity' }"><strong>数量账</strong><span>维护数量流水与可用量</span></RouterLink>
          <RouterLink :to="{ name: 'workbench.allocation' }"><strong>样品分配</strong><span>绑定门控版本并分配测试对象</span></RouterLink>
          <RouterLink :to="{ name: 'workbench.batch' }"><strong>批次管理</strong><span>组织成员、证据与冻结状态</span></RouterLink>
          <RouterLink :to="{ name: 'workbench.instrument' }"><strong>仪器导入</strong><span>登记文件、解析行并处理异常</span></RouterLink>
          <RouterLink :to="{ name: 'workbench.result' }"><strong>结果采用</strong><span>维护来源、推导、规则与采用版本</span></RouterLink>
          <RouterLink :to="{ name: 'workbench.qc' }"><strong>QC 放行</strong><span>传播影响并满足五个放行门</span></RouterLink>
          <RouterLink :to="{ name: 'workbench.report' }"><strong>报告签发</strong><span>评估门禁、签发并验证版本链</span></RouterLink>
        </div>
      </section>
    </a-card>
  </main>
</template>
