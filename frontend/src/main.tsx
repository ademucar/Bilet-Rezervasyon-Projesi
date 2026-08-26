import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App'

const rootElement = document.getElementById('root')

if (!rootElement) {
  // Bu asla olmamali ama olursa bembeyaz ekran yerine anlamli bir
  // hata gormek isterim. "root bulunamadi" mesaji, index.html'de bir
  // sorun oldugunu aninda soyler.
  throw new Error('#root elemani bulunamadi. index.html kontrol edin.')
}

createRoot(rootElement).render(
  // StrictMode gelistirmede bilesenleri IKI KEZ render eder.
  // Bu kasitlidir: yan etkisi olan (idempotent olmayan) kodu
  // ortaya cikarir. Uretim derlemesinde devre disi kalir.
  <StrictMode>
    <App />
  </StrictMode>,
)
