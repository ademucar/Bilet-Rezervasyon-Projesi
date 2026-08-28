import { Link } from 'react-router-dom'
import { useAuthStore } from '../../stores/authStore'
import { AuthLayout } from '../auth/components/AuthLayout'
import { Alert } from '../../components/ui/Alert'

/** PDF Sprint 3: "Yetkisiz erisim ekrani". */
export function UnauthorizedPage() {
  const user = useAuthStore((s) => s.user)

  return (
    <AuthLayout
      title="Bu sayfaya erisiminiz yok"
      footer={
        <Link to="/" className="font-medium text-brand-600 hover:underline">
          Ana sayfaya don
        </Link>
      }
    >
      <Alert variant="error">Bu sayfayi goruntulemek icin gerekli yetkiye sahip degilsiniz.</Alert>

      {user && (
        // Mevcut rolu GOSTERIYORUM. Kullanici "ama ben organizatorum"
        // derken aslinda rolunun verilmedigini boylece gorebiliyor.
        // Destek talebi acmadan once sorunu kendisi anlayabiliyor.
        <p className="mt-4 text-sm text-slate-500">
          <strong>{user.email}</strong> hesabiyla giris yaptiniz. Mevcut rolleriniz:{' '}
          {user.roles.join(', ') || 'yok'}
        </p>
      )}
    </AuthLayout>
  )
}
