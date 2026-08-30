import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App'

const rootElement = document.getElementById('root')

if (!rootElement) {
  // Bu asla olmamali ama olursa bembeyaz ekran yerine anlamlı bir
  // hata gormek isterim. "root bulunamadı" mesaji, index.html'de bir
  // sorun olduğunu anında söyler.
  throw new Error('#root elemani bulunamadi. index.html kontrol edin.')
}

createRoot(rootElement).render(
  // StrictMode gelistirmede bilesenleri iki kez render eder.
  // Bu kasitlidir: yan etkisi olan (idempotent olmayan) kodu
  // ortaya cikarir. Üretim derlemesinde devre dışı kalır.
  <StrictMode>
    <App />
  </StrictMode>,
)
