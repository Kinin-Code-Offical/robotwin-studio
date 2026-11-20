using System.Collections.Generic;

namespace RobotWin.Studio.Editor
{
    /// <summary>The schematic editor's hotkeys, grouped for the "Kısayollar" (F1 / ?) overlay.</summary>
    public static class SchematicShortcuts
    {
        public static readonly IReadOnlyList<(string Group, IReadOnlyList<(string Keys, string What)> Items)> All = new List<(string, IReadOnlyList<(string, string)>)>
        {
            ("Ekle", new List<(string, string)>
            {
                ("A", "Parça ekle (aranabilir liste)"),
                ("P", "Güç sembolü (GND, +5V…)"),
                ("W", "Kablo aracı · Shift+W imleçten başlat"),
                ("L / Ctrl+L", "Hat etiketi / global etiket"),
                ("Q", "Bağlantı yok (NC) işareti"),
                ("Pine tık", "Kablo başlat"),
            }),
            ("Kablo", new List<(string, string)>
            {
                ("/", "Köşe yönü (önce yatay / dikey)"),
                ("Shift+Boşluk", "Mod: 90° → 45° → serbest"),
                ("Çift tık · Esc", "Kabloyu bitir"),
                ("Backspace", "Son köşeyi geri al"),
                ("Sağ tık kablo", "Böl · segment sil · etiket"),
            }),
            ("Düzenle", new List<(string, string)>
            {
                ("M / G", "Taşı / sürükle (kablolar uzar)"),
                ("R · Shift+R", "Döndür (saat yönü tersi / yönü)"),
                ("X · Y", "Yatay · dikey aynala"),
                ("Del", "Sil"),
                ("Ctrl+D · C", "Çoğalt (imleci izler)"),
                ("Ctrl+C / X / V", "Kopyala / kes / yapıştır (sekmeler arası)"),
                ("Oklar · Shift", "Seçimi 1 / 10 ızgara kaydır"),
                ("Ctrl+Z · Ctrl+Y", "Geri al · yinele"),
            }),
            ("Özellikler", new List<(string, string)>
            {
                ("E", "Özellikler (çoklu seçim)"),
                ("V · F2 · çift tık", "Değeri yerinde düzenle"),
                ("D", "Datasheet"),
                ("Ctrl+F · Ctrl+H", "Bul · bul ve değiştir"),
            }),
            ("Seçim", new List<(string, string)>
            {
                ("Tık · Shift · Ctrl", "Seç · ekle · tersine çevir"),
                ("Sürükle →", "Tamamen içeridekiler"),
                ("Sürükle ←", "Kesişenler"),
                ("Ctrl+A · Esc", "Tümünü seç · seçimi kaldır"),
                ("U · çift tık kablo", "Bağlantıyı seç"),
                ("Tab · Shift+Tab", "Parçalar arasında gez"),
            }),
            ("Görünüm", new List<(string, string)>
            {
                ("F · Home", "Sığdır"),
                ("+ / − · tekerlek", "Yakınlaştır (imlece)"),
                ("Orta tuş · Boşluk+sürükle", "Kaydır"),
                ("`", "İmleç altındaki hattı vurgula"),
                ("Boşluk", "Göreli başlangıç (dX/dY)"),
                ("Ctrl+Shift+X", "Artı imleç"),
                ("Ctrl+P", "Yazdır / PDF"),
                ("F5 · Shift+F5", "Simülasyon başlat · durdur"),
            }),
        };
    }
}
