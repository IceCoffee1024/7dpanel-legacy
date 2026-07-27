<script setup lang="ts">
import type { PurchaseProductInput, ShopProductDraft } from '../../features/commerce/api/commerce'
import { ShopProductsView, useShopProducts } from '../../features/commerce'
const controller = useShopProducts(); function load(id: string) { void controller.load(id) } function save(draft: ShopProductDraft) { void controller.save(draft) } function purchase(input: PurchaseProductInput) { void controller.purchaseProduct(input) }
</script><template><ShopProductsView :controller="controller" @load="load" @save="save" @purchase="purchase" /></template>

<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>
