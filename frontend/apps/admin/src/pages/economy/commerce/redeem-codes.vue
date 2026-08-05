<script setup lang="ts">
import type { CreateRedeemCodeInput } from '../../../features/commerce/api/commerce'
import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { RedeemCodesView, useRedeemCodes } from '../../../features/commerce'

const controller = useRedeemCodes()
function load(id: string) {
  void controller.load(id)
}
function create(input: CreateRedeemCodeInput) {
  void controller.create(input)
}
</script>

<template>
  <NavigationSectionTabs />
  <RedeemCodesView
    :controller="controller"
    @load="load"
    @create="create"
    @clear-generated="controller.clearGenerated"
  />
</template>

<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>
