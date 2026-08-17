<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import steamIcon from '../assets/steam.svg'

const route = useRoute()

const steamLoginUrl = computed(() => {
  return `/api/oauth/steam/login?redirect=${encodeURIComponent('/player/store')}`
})

const errorMessage = computed(() => {
  const error = route.query.error
  if (typeof error !== 'string')
    return ''

  const messages: Readonly<Record<string, string>> = {
    invalid_login_state: '登录请求已失效，请重新登录。',
    steam_login_cancelled: 'Steam 登录已取消。',
    steam_verification_failed: 'Steam 身份验证失败，请重试。',
    invalid_steam_response: 'Steam 返回了无效的登录信息。',
    player_not_found: '该 Steam 账号没有匹配的游戏玩家记录。',
    game_unavailable: '暂时无法读取游戏玩家数据，请稍后重试。',
  }
  return messages[error] ?? '登录未完成，请重试。'
})
</script>

<template>
  <main class="login-page">
    <section class="login-panel" aria-labelledby="login-title">
      <p class="brand-mark">
        7D
      </p>
      <p class="eyebrow">
        7DPanel
      </p>
      <h1 id="login-title" class="login-title">
        玩家商店
      </h1>
      <p class="login-copy">
        使用你的 Steam 账号进入服务器商店。
      </p>

      <a class="steam-button" :href="steamLoginUrl">
        <img class="steam-icon" :src="steamIcon" alt="">
        <span>使用 Steam 登录</span>
      </a>
      <p v-if="errorMessage" class="login-error" role="alert">
        {{ errorMessage }}
      </p>
    </section>
  </main>
</template>

<style scoped>
.login-page {
  min-height: 100vh;
  padding: 24px;
  display: grid;
  place-items: center;
  background:
    linear-gradient(rgba(23, 25, 22, 0.82), rgba(23, 25, 22, 0.96)),
    repeating-linear-gradient(135deg, #34372f 0 1px, transparent 1px 18px);
}

.login-panel {
  width: min(100%, 420px);
  padding: 40px;
  border: 1px solid #4b5045;
  border-radius: 8px;
  background: #22251f;
  box-shadow: 0 24px 70px rgba(0, 0, 0, 0.36);
}

.brand-mark {
  width: 52px;
  height: 52px;
  margin: 0 0 28px;
  display: grid;
  place-items: center;
  border: 1px solid #88956f;
  color: #dce8c4;
  font-weight: 800;
  font-size: 19px;
}

.eyebrow {
  margin: 0 0 8px;
  color: #aab69a;
  font-size: 13px;
  font-weight: 700;
  text-transform: uppercase;
}

.login-title {
  margin: 0;
  color: #fffdf5;
  font-size: 32px;
  line-height: 1.2;
}

.login-copy {
  margin: 14px 0 32px;
  color: #b9bcb4;
  line-height: 1.7;
}

.steam-button {
  width: 100%;
  min-height: 52px;
  padding: 10px 18px;
  border: 1px solid #2f7196;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  color: #ffffff;
  background: #174f70;
  cursor: pointer;
  font-weight: 700;
  text-decoration: none;
  transition: background-color 160ms ease, border-color 160ms ease;
}

.steam-button:hover {
  border-color: #65a7c9;
  background: #206789;
}

.steam-icon {
  width: 28px;
  height: 28px;
}

.login-error {
  margin: 18px 0 0;
  color: #e7a39c;
  font-size: 14px;
  line-height: 1.6;
}

@media (max-width: 480px) {
  .login-page {
    padding: 16px;
  }

  .login-panel {
    padding: 28px 24px;
  }
}
</style>
