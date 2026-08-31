import { describe, expect, it } from 'vitest'
import { slugOner } from './referenceApi'

/**
 * Slug önerisi.
 *
 * Bunu test etmemin sebebi Türkçe: JavaScript'in varsayılan
 * küçültme ve aksan ayırma davranışları Türkçede yanlış sonuç
 * veriyor ve hata gözle fark edilmiyor -- "işik" ile "isik" arasında
 * tek bir nokta var ama biri çalışan, diğeri kırık bir adres.
 */
describe('slugOner', () => {
  it('boşlukları tireye çevirir', () => {
    expect(slugOner('Rock Konseri')).toBe('rock-konseri')
  })

  it('Türkçe harfleri ASCII karşılığına çevirir', () => {
    expect(slugOner('Çocuk Tiyatrosu')).toBe('cocuk-tiyatrosu')
    expect(slugOner('Şiir Günü')).toBe('siir-gunu')
    expect(slugOner('Öğrenci Şenliği')).toBe('ogrenci-senligi')
  })

  it('büyük I harfini Türkçe kuralına göre küçültür', () => {
    // Asıl tuzak burada. Normal toLowerCase 'I' -> 'i' yapar, ama
    // Türkçede 'I'nın küçüğü 'ı'dır. Eşleme tablosunda 'ı' var,
    // 'i' de var; ikisi de 'i'ye gidiyor. Yanlış küçültme olsaydı
    // sonuç yine 'isik' çıkardı -- yani bu test kuralı değil,
    // SONUCU koruyor: hangi yolla olursa olsun ASCII çıkmalı.
    expect(slugOner('IŞIK')).toBe('isik')
    expect(slugOner('İstanbul Festivali')).toBe('istanbul-festivali')
  })

  it('art arda boşluk ve noktalamayı tek tireye indirir', () => {
    // Backend "rock--konseri" biçimini reddediyor: iki farklı slug
    // gözle ayırt edilemez hale gelirdi.
    expect(slugOner('Rock  &  Metal')).toBe('rock-metal')
    expect(slugOner('Stand-up / Komedi')).toBe('stand-up-komedi')
  })

  it('baştaki ve sondaki tireleri atar', () => {
    expect(slugOner('  Konser  ')).toBe('konser')
    expect(slugOner('!Festival!')).toBe('festival')
  })

  it('boş girdide boş döner', () => {
    // Buton zaten boş slug'da kapalı; yine de çökmemeli.
    expect(slugOner('')).toBe('')
    expect(slugOner('!!!')).toBe('')
  })
})
