import { defineStore } from 'pinia'
import { ref } from 'vue'

const STORAGE_KEY = 'mindmap-theme'

export const useThemeStore = defineStore('theme', () => {
  const isDark = ref(false)

  function apply() {
    document.documentElement.classList.toggle('dark', isDark.value)
    document.documentElement.setAttribute('data-theme', isDark.value ? 'dark' : 'light')
  }

  function init() {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved === 'dark' || saved === 'light') {
      isDark.value = saved === 'dark'
    } else {
      isDark.value = window.matchMedia('(prefers-color-scheme: dark)').matches
    }
    apply()
  }

  function toggle() {
    isDark.value = !isDark.value
    localStorage.setItem(STORAGE_KEY, isDark.value ? 'dark' : 'light')
    apply()
  }

  return { isDark, init, toggle }
})
