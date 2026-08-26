import { Link } from 'react-router-dom'
import { AuthLayout } from '../auth/components/AuthLayout'
import { Alert } from '../../components/ui/Alert'

export function NotFoundPage() {
  return (
    <AuthLayout
      title="Sayfa bulunamadi"
      footer={
        <Link to="/" className="font-medium text-brand-600 hover:underline">
          Ana sayfaya don
        </Link>
      }
    >
      <Alert variant="info">
        Aradiginiz sayfa tasinmis veya hic var olmamis olabilir.
      </Alert>
    </AuthLayout>
  )
}
