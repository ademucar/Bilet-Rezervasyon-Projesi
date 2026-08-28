import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { SeatMap, type SeatMapSection } from './SeatMap';

/**
 * PDF Sprint 17 frontend testi: "Koltuk seçimi".
 */

function bolum(): SeatMapSection[] {
  return [
    {
      id: 'blok-1',
      name: 'Orta Blok',
      displayOrder: 1,
      seats: [
        {
          id: 'koltuk-1',
          rowLabel: 'A',
          seatNumber: 1,
          label: 'A-1',
          fill: '#22c55e',
          selectable: true,
        },
        {
          id: 'koltuk-2',
          rowLabel: 'A',
          seatNumber: 2,
          label: 'A-2',
          fill: '#ef4444',
          selectable: false,
          description: 'satıldı',
        },
        {
          id: 'koltuk-3',
          rowLabel: 'A',
          seatNumber: 3,
          label: 'A-3',
          fill: '#22c55e',
          selectable: true,
        },
      ],
    },
  ];
}

describe('SeatMap', () => {
  /**
   * ================================================================
   * ERİŞİLEBİLİR AD, MODA GÖRE FARKLI YERDEN GELİYOR
   * ================================================================
   * İlk yazdığımda salt okunur haritada getByLabelText kullandım ve
   * test "Unable to find a label" diye kırıldı.
   *
   * Sebep bileşenin bilinçli bir tasarımı:
   *   - Etkileşimli modda  -> aria-label + role="button"
   *   - Salt okunur modda  -> yalnızca SVG <title>
   *
   * Salt okunur haritaya role="button" vermek YANLIŞ olurdu: ekran
   * okuyucu kullanıcısına tıklanabilir bir şey vaat edip hiçbir şey
   * yapmamak, hiç etiket vermemekten daha kötü.
   *
   * Yani test kırılması bileşende hata olduğunu değil, iki modun
   * gerçekten farklı olduğunu gösterdi.
   * ================================================================
   */
  it('salt okunur haritada koltuklar başlıkla tanımlanır', () => {
    const { container } = render(<SeatMap sections={bolum()} />);

    // SVG <title> içeriğini doğrudan okuyoruz.
    //
    // getByTitle ile de denedim ama başlık metni JSX'te parçalara
    // ayrılmış ({section.name} - {seat.label}) ve DOM'da birden
    // fazla metin düğümü olarak duruyor; sorgu eşleştiremiyor.
    const basliklar = [...container.querySelectorAll('title')].map(
      (t) => t.textContent ?? '',
    );

    expect(basliklar).toHaveLength(3);
    expect(basliklar.join(' ')).toContain('A-1');
    expect(basliklar.join(' ')).toContain('A-2');

    // Satılmış koltuğun durumu da başlıkta yazmalı: ekran okuyucu
    // kullanıcısı koltuğun neden seçilemediğini rengi görerek
    // anlayamaz.
    expect(basliklar.join(' ')).toContain('satıldı');
  });

  it('etkileşimli haritada koltuklar erişilebilir adla çizilir', () => {
    render(<SeatMap sections={bolum()} onSeatClick={vi.fn()} />);

    expect(screen.getByLabelText(/A-1/)).toBeInTheDocument();
    expect(screen.getByLabelText(/A-2/)).toBeInTheDocument();
    expect(screen.getByLabelText(/A-3/)).toBeInTheDocument();
  });

  it('müsait koltuğa tıklanınca id ile haber verir', async () => {
    const tiklama = vi.fn();
    const kullanici = userEvent.setup();

    render(<SeatMap sections={bolum()} onSeatClick={tiklama} />);

    await kullanici.click(screen.getByLabelText(/A-1/));

    expect(tiklama).toHaveBeenCalledExactlyOnceWith('koltuk-1');
  });

  /**
   * ================================================================
   * SATILMIŞ KOLTUK TIKLANAMAMALI
   * ================================================================
   * Sunucu zaten reddeder — ama kullanıcıya tıklatıp sonra hata
   * göstermek kötü bir deneyim. Daha da önemlisi: satılmış koltuğu
   * seçilebilir göstermek, kullanıcıya olmayan bir koltuğu vaat
   * etmek demek.
   *
   * Bu, "sunucu nasılsa kontrol ediyor" diyerek atlanabilecek bir
   * kontrol değil: iki katman FARKLI şeyler için var. Sunucu veri
   * bütünlüğünü, arayüz kullanıcının zamanını koruyor.
   * ================================================================
   */
  it('satılmış koltuğa tıklanamaz', async () => {
    const tiklama = vi.fn();
    const kullanici = userEvent.setup();

    render(<SeatMap sections={bolum()} onSeatClick={tiklama} />);

    await kullanici.click(screen.getByLabelText(/A-2/));

    expect(tiklama).not.toHaveBeenCalled();
  });

  /**
   * Seçili koltuk ekran okuyucuya da bildirilmeli.
   *
   * aria-pressed olmadan görme engelli bir kullanıcı hangi
   * koltukları seçtiğini bilemez — renk çerçevesi ona hiçbir şey
   * ifade etmiyor.
   */
  it('seçili koltuğu aria-pressed ile bildirir', () => {
    render(
      <SeatMap
        sections={bolum()}
        onSeatClick={vi.fn()}
        selectedSeatIds={new Set(['koltuk-1'])}
      />,
    );

    expect(screen.getByLabelText(/A-1/)).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByLabelText(/A-3/)).toHaveAttribute('aria-pressed', 'false');
  });

  /**
   * ================================================================
   * KLAVYE ERİŞİMİ
   * ================================================================
   * Koltuk seçimi yalnızca fareyle yapılabilseydi, klavye kullanan
   * herkes (motor engelli kullanıcılar, ekran okuyucu kullananlar)
   * bilet alamazdı.
   *
   * Koltukları <button> olarak çizdiğimiz için bu bedava geliyor —
   * ama "bedava geliyor" varsayımını test ediyorum, çünkü biri
   * ilerde performans için <div>'e çevirmek isteyebilir.
   * ================================================================
   */
  it('koltuklar klavyeyle seçilebilir', async () => {
    const tiklama = vi.fn();
    const kullanici = userEvent.setup();

    render(<SeatMap sections={bolum()} onSeatClick={tiklama} />);

    screen.getByLabelText(/A-1/).focus();
    await kullanici.keyboard('{Enter}');

    expect(tiklama).toHaveBeenCalledWith('koltuk-1');
  });

  it('koltuk yoksa açıklayıcı mesaj gösterir', () => {
    render(<SeatMap sections={[]} emptyMessage="Bu oturumda koltuk yok." />);

    expect(screen.getByText('Bu oturumda koltuk yok.')).toBeInTheDocument();
  });

  /**
   * onSeatClick verilmezse harita salt okunur olmalı.
   *
   * Admin panelindeki önizleme bu modu kullanıyor: düzeni gösteriyor
   * ama seçim yaptırmıyor.
   */
  it('tıklama işleyicisi yoksa koltuklar buton olmaz', () => {
    render(<SeatMap sections={bolum()} />);

    expect(screen.queryAllByRole('button')).toHaveLength(0);
  });
});
