<template>
  <UDashboardPanel id="overview">
    <template #header>
      <UDashboardNavbar title="概览">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <section class="mx-auto flex w-full max-w-5xl flex-1 items-center justify-center py-16">
        <div class="flex max-w-md flex-col items-center text-center">
          <span class="mb-5 flex size-12 items-center justify-center rounded-md bg-elevated text-muted">
            <UIcon name="i-lucide-server-off" class="size-6" />
          </span>
          <h2 class="text-base font-semibold text-highlighted">
            服务状态不可用
          </h2>
          <p class="mt-2 text-sm text-muted">
            尚未从后端获取服务器状态。
          </p>
        </div>
      </section>
    </template>
  </UDashboardPanel>
</template>
