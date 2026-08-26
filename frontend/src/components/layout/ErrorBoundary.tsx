import { Component, type ErrorInfo, type ReactNode } from 'react'

interface Props {
  children: ReactNode
}

interface State {
  hasError: boolean
}

/**
 * PDF Sprint 18: "Error Boundary kullanilmalidir."
 *
 * ==================================================================
 * NEDEN SINIF BILESENI? Hook'lar varken?
 * ==================================================================
 * Cunku React'in hata yakalama mekanizmasi (componentDidCatch ve
 * getDerivedStateFromError) YALNIZCA sinif bilesenlerinde calisir.
 * Bunlarin hook karsiligi henuz YOK.
 *
 * Yani bu, "eski usul kod" degil; React'in bugun bile tek yolu.
 *
 * NE ISE YARIYOR? Bir bilesende yakalanmamis hata olursa, React
 * varsayilan olarak TUM uygulamayi soker ve kullanici bembeyaz bir
 * ekran gorur. Error Boundary o hatayi yakalayip anlamli bir arayuz
 * gosterir.
 * ==================================================================
 */
export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props)
    this.state = { hasError: false }
  }

  static getDerivedStateFromError(): State {
    return { hasError: true }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    // Sprint 16'da bu hatalari sunucuya gonderecegiz.
    // Su an konsola yaziyoruz ki gelistirme sirasinda gorelim --
    // sessizce yutmak en kotusu olurdu.
    console.error('Yakalanmamis hata:', error, errorInfo)
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="flex min-h-screen items-center justify-center px-4">
          <div className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-8 text-center shadow-sm">
            <h1 className="text-lg font-semibold text-slate-900">Bir seyler ters gitti</h1>
            <p className="mt-2 text-sm text-slate-500">
              Beklenmeyen bir hata olustu. Sayfayi yenilemeyi deneyin.
            </p>

            <button
              type="button"
              // Basit ve etkili: tam sayfa yenileme, bozulmus tum
              // uygulama durumunu sifirlar. setState ile "kurtarmaya"
              // calismak, hataya sebep olan durumun kalmasi riskini tasir.
              onClick={() => window.location.reload()}
              className="mt-6 rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-brand-700"
            >
              Sayfayi yenile
            </button>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}
