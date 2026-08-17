<script setup lang="ts">
import { shallowRef } from 'vue'

import { usePlayerSession } from '../features/auth/model/usePlayerSession'

interface StoreItem {
  id: string
  name: string
  description: string
  price: number
  mark: string
  color: string
}

const items: readonly StoreItem[] = [
  { id: 'medical-kit', name: '急救物资包', description: '绷带、止痛药和急救药品。', price: 120, mark: 'MED', color: '#b84d43' },
  { id: 'ammo-pack', name: '7.62mm 弹药包', description: '适合步枪使用的基础弹药。', price: 180, mark: 'AMMO', color: '#b88a3b' },
  { id: 'tool-set', name: '铁制工具组', description: '包含铁镐、铁斧和修理工具。', price: 260, mark: 'TOOL', color: '#547b75' },
  { id: 'fuel-can', name: '载具燃料', description: '一组用于载具补给的汽油。', price: 90, mark: 'FUEL', color: '#75884e' },
]

const purchaseNotice = shallowRef('')
const { session, status, isLoggingOut, logoutError, logout } = usePlayerSession()

function selectItem(item: StoreItem) {
  purchaseNotice.value = `${item.name}当前不可购买。`
}
</script>

<template>
  <main class="store-page">
    <header class="store-header">
      <div class="header-inner">
        <div>
          <p class="eyebrow">
            7DPanel
          </p>
          <h1 class="store-title">
            玩家商店
          </h1>
        </div>

        <div class="player-actions">
          <dl class="player-summary">
            <div class="summary-item">
              <dt>玩家</dt>
              <dd v-if="status === 'authenticated'">
                {{ session?.displayName }}
              </dd>
              <dd v-else-if="status === 'loading'" class="session-status">
                正在验证...
              </dd>
              <dd v-else class="session-error">
                玩家信息不可用
              </dd>
            </div>
            <div class="summary-item">
              <dt>余额</dt>
              <dd class="balance">850 代币</dd>
            </div>
          </dl>
          <div class="logout-control">
            <button
              class="logout-button"
              type="button"
              :disabled="status !== 'authenticated' || isLoggingOut"
              @click="logout"
            >
              {{ isLoggingOut ? '退出中...' : '退出登录' }}
            </button>
            <p class="logout-error" aria-live="polite">
              {{ logoutError }}
            </p>
          </div>
        </div>
      </div>
    </header>

    <section class="catalog" aria-labelledby="catalog-title">
      <div class="catalog-heading">
        <div>
          <p class="section-label">
            补给目录
          </p>
          <h2 id="catalog-title" class="catalog-title">
            生存物资
          </h2>
        </div>
        <p class="purchase-notice" aria-live="polite">
          {{ purchaseNotice }}
        </p>
      </div>

      <div class="item-grid">
        <article v-for="item in items" :key="item.id" class="item-card">
          <div class="item-visual" :style="{ backgroundColor: item.color }">
            {{ item.mark }}
          </div>
          <div class="item-content">
            <h3 class="item-name">
              {{ item.name }}
            </h3>
            <p class="item-description">
              {{ item.description }}
            </p>
            <div class="item-footer">
              <span class="item-price">{{ item.price }} 代币</span>
              <button class="buy-button" type="button" @click="selectItem(item)">
                购买
              </button>
            </div>
          </div>
        </article>
      </div>
    </section>
  </main>
</template>

<style scoped>
.store-page {
  min-height: 100vh;
  color: #eeeae0;
  background: #191b18;
}

.store-header {
  border-bottom: 1px solid #3e423a;
  background: #232620;
}

.header-inner,
.catalog {
  width: min(1120px, calc(100% - 40px));
  margin: 0 auto;
}

.header-inner {
  min-height: 112px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 32px;
}

.eyebrow,
.section-label {
  margin: 0 0 6px;
  color: #aab69a;
  font-size: 12px;
  font-weight: 800;
  text-transform: uppercase;
}

.store-title,
.catalog-title,
.item-name {
  margin: 0;
}

.store-title {
  font-size: 28px;
}

.player-summary {
  margin: 0;
  display: flex;
  gap: 32px;
}

.player-actions {
  display: flex;
  align-items: center;
  gap: 28px;
}

.logout-control {
  min-width: 82px;
}

.logout-button {
  min-width: 82px;
  min-height: 34px;
  padding: 6px 10px;
  border: 1px solid #5d6258;
  border-radius: 5px;
  color: #e8eae3;
  background: #343830;
  cursor: pointer;
  font-weight: 700;
}

.logout-button:hover:not(:disabled) {
  border-color: #888f80;
  background: #41463c;
}

.logout-button:disabled {
  cursor: default;
  opacity: 0.55;
}

.logout-error {
  min-height: 16px;
  margin: 4px 0 0;
  color: #e7a39c;
  font-size: 11px;
  white-space: nowrap;
}

.summary-item dt {
  margin-bottom: 5px;
  color: #999d94;
  font-size: 12px;
}

.summary-item dd {
  margin: 0;
  font-weight: 700;
}

.session-status {
  color: #b9bcb4;
}

.session-error {
  color: #e7a39c;
}

.balance,
.item-price {
  color: #f0bd57;
}

.catalog {
  padding-top: 44px;
  padding-bottom: 64px;
}

.catalog-heading {
  min-height: 58px;
  margin-bottom: 24px;
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 24px;
}

.catalog-title {
  font-size: 22px;
}

.purchase-notice {
  min-height: 22px;
  margin: 0;
  color: #c8cbbf;
  font-size: 14px;
  text-align: right;
}

.item-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.item-card {
  min-width: 0;
  min-height: 190px;
  border: 1px solid #41453d;
  border-radius: 6px;
  display: grid;
  grid-template-columns: 132px minmax(0, 1fr);
  overflow: hidden;
  background: #252822;
}

.item-visual {
  min-height: 100%;
  display: grid;
  place-items: center;
  color: rgba(255, 255, 255, 0.9);
  font-size: 14px;
  font-weight: 900;
}

.item-content {
  min-width: 0;
  padding: 22px;
  display: flex;
  flex-direction: column;
}

.item-name {
  color: #fffdf5;
  font-size: 18px;
}

.item-description {
  margin: 9px 0 18px;
  color: #adb0a8;
  font-size: 14px;
  line-height: 1.6;
}

.item-footer {
  margin-top: auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.item-price {
  font-weight: 800;
}

.buy-button {
  min-width: 74px;
  min-height: 38px;
  padding: 8px 14px;
  border: 1px solid #77845f;
  border-radius: 5px;
  color: #f2f6ea;
  background: #4c5b3b;
  cursor: pointer;
  font-weight: 700;
  transition: background-color 160ms ease, border-color 160ms ease;
}

.buy-button:hover {
  border-color: #a6b887;
  background: #61754a;
}

@media (max-width: 760px) {
  .header-inner {
    padding: 22px 0;
    align-items: flex-start;
    flex-direction: column;
    gap: 20px;
  }

  .player-summary {
    flex: 1;
    justify-content: space-between;
    gap: 20px;
  }

  .player-actions {
    width: 100%;
    align-items: flex-start;
  }

  .catalog-heading {
    align-items: flex-start;
    flex-direction: column;
    gap: 12px;
  }

  .purchase-notice {
    text-align: left;
  }

  .item-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 440px) {
  .header-inner,
  .catalog {
    width: min(100% - 28px, 1120px);
  }

  .item-card {
    grid-template-columns: 1fr;
  }

  .item-visual {
    min-height: 92px;
  }
}
</style>
