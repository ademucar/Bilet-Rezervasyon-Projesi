import { Link } from 'react-router-dom'
import { useAuthStore } from '../../stores/authStore'
import { AuthLayout } from '../auth/components/AuthLayout'
import { Alert } from '../../components/ui/Alert'

/** PDF Sprint 3: "Yetkisiz erişim ekrani". */
export function UnauthorizedPage() {
  const user = useAuthStore((s) => s.user)

  return (
    <AuthLayout
      title="Bu sayfaya erişiminiz yok"
      footer={
        <Link to="/" className="font-medium text-brand-600 hover:underline">
          Ana sayfaya dön
        </Link>
      }
    >
      <Alert variant="error">Bu sayfayı görüntülemek için gerekli yetkiye sahip değilsiniz.</Alert>

      {user && (
        // Mevcut rolü GOSTERIYORUM. Kullanıcı "ama ben organizatorum"
        // derken aslında rolunun verilmedigini boylece gorebiliyor.
        // Destek talebi acmadan önce sorunu kendisi anlayabiliyor.
        <p className="mt-4 text-sm text-slate-500">
          <strong>{user.email}</strong> hesabiyla giris yaptiniz. Mevcut rolleriniz:{' '}
          {user.roles.join(', ') || 'yok'}
        </p>
      )}
    </AuthLayout>
  )
}
