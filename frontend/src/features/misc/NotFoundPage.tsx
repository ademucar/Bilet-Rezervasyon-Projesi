import { Link } from 'react-router-dom'
import { AuthLayout } from '../auth/components/AuthLayout'
import { Alert } from '../../components/ui/Alert'

export function NotFoundPage() {
  return (
    <AuthLayout
      title="Sayfa bulunamadı"
      footer={
        <Link to="/" className="font-medium text-brand-600 hover:underline">
          Ana sayfaya dön
        </Link>
      }
    >
      <Alert variant="info">Aradığınız sayfa taşınmış veya hiç var olmamış olabilir.</Alert>
    </AuthLayout>
  )
}
