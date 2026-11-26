namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [keys] The keys of the schematic and PCB editors, rebindable in Ayarlar → Klavye kısayolları. Each page keeps its
    /// default keys in its own key handler; <see cref="Shortcuts.Translate"/> turns a chosen key into the default one
    /// before the page sees it, so a tool moves to another key without the page knowing. Single letters are fine here:
    /// a page's keys only work while its canvas has the focus, not while typing in a box.
    /// </summary>
    internal static class PageShortcuts
    {
        public const string Schematic = "devre";
        public const string Pcb = "pcb";
        public const string World = "dunya";
        public const string Code = "kod";
        public const string Model = "parca";

        public static void RegisterAll()
        {
            void S(string id, string title, string keys) => Shortcuts.RegisterPage(Schematic, "devre." + id, "Devre sayfası", title, keys);
            S("parca", "Parça ekle", "A");
            S("guc", "Güç sembolü ekle", "P");
            S("kablo", "Kablo çiz", "W");
            S("etiket", "Hat etiketi", "L");
            S("nc", "Bağlantısız (NC) işareti", "Q");
            S("metin", "Metin ekle", "T");   // [notes]
            S("tasi", "Taşı", "M");
            S("surukle", "Kablolarıyla sürükle", "G");
            S("dondur", "Döndür", "R");
            S("aynala-x", "Yatay aynala", "X");
            S("aynala-y", "Dikey aynala", "Y");
            S("ozellik", "Özellikler", "E");
            S("deger", "Değeri düzenle", "V");
            S("datasheet", "Veri sayfası", "D");
            S("cogalt", "Çoğalt", "C");
            S("sil", "Sil", "Del");
            S("sigdir", "Ekrana sığdır", "F");
            S("hat", "İmlecin altındaki hattı vurgula", "`");

            void P(string id, string title, string keys) => Shortcuts.RegisterPage(Pcb, "pcb." + id, "PCB sayfası", title, keys);
            P("rota", "Rota çiz", "X");
            P("dolgu", "Bakır dolgu çiz", "Z");
            P("yazi", "Yazı ekle", "T");
            P("dolgu-yenile", "Dolguları yeniden hesapla", "B");
            P("katman-tek", "Yalnız etkin katmanı göster", "H");
            P("katman-degistir", "Üst / alt bakır arasında geç", "V");
            P("dondur", "Döndür", "R");
            P("cevir", "Kartın öbür yüzüne çevir", "F");
            P("kilit", "Kilitle / kilidi aç", "L");
            P("tasi", "Taşı", "M");
            P("surukle", "Yollarıyla sürükle", "G");
            P("yol-surukle", "Yol parçasını sürükle", "D");
            P("tum-yol", "Bütün yolu seç", "U");
            P("genislik", "Yol genişliğini değiştir", "W");
            P("ozellik", "Özellikler", "E");
            P("sil", "Sil", "Del");
            P("sigdir", "Ekrana sığdır", "Home");

            void W(string id, string title, string keys) => Shortcuts.RegisterPage(World, "dunya." + id, "Dünya sayfası", title, keys);
            W("kablo", "Kablo aracı", "K");
            W("bagla", "Bağla aracı", "B");
            W("dondur", "Döndür (15°; Shift ile geri)", "Ctrl+E");
            W("tutamac-tasi", "Taşı tutamacı", "W");   // [gizmo] Unity's keys
            W("tutamac-dondur", "Döndür tutamacı", "E");
            W("tutamac-olcekle", "Ölçekle tutamacı", "R");
            W("tutamac-hepsi", "Taşı, döndür, ölçekle tutamaçları", "Y");
            W("tutamac-yok", "Yalnız seç (tutamaçsız)", "Q");
            W("cogalt", "Çoğalt", "Ctrl+D");
            W("sil", "Sil", "Del");
            W("takip", "Kamera seçili nesneyi izlesin", "T");
            W("odakla", "Seçiliye ya da her şeye odaklan", "F");
            W("kayit", "Kayıt al / bitir", "Ctrl+R");

            // The code page: keys with a modifier or function keys (plain letters go into the code).
            void C(string id, string title, string keys) => Shortcuts.RegisterPage(Code, "kod." + id, "Kod sayfası", title, keys);
            C("derle", "Derle", "F7");
            C("yukle", "Karta yükle ve çalıştır (durmuşken: devam)", "F5");
            C("adim", "Hata ayıklama: satırı geç", "F10");
            C("icine", "Hata ayıklama: işlevin içine gir", "F11");
            C("disina", "Hata ayıklama: işlevden çık", "Shift+F11");

            void M(string id, string title, string keys) => Shortcuts.RegisterPage(Model, "parca." + id, "Parça sayfası", title, keys);
            M("kip", "Nesne / düzenleme kipi", "Tab");
            M("ekle", "Ekle menüsü", "Shift+A");
            M("hepsi", "Hepsini seç", "A");
            M("tasi", "Taşı", "G");
            M("dondur", "Döndür", "R");
            M("olcekle", "Ölçekle", "S");
            M("disari", "Dışarı çek (düzenleme kipi)", "E");
            M("icine", "İçe kaydır (düzenleme kipi)", "I");
            M("bicak", "Bıçak", "K");
            M("halka", "Halka kesimi (düzenleme kipi)", "Ctrl+R");
            M("pah", "Pah (düzenleme kipi)", "Ctrl+B");
            M("cogalt", "Çoğalt", "Shift+D");
            M("birlestir", "Birleştir", "Ctrl+J");
            M("sil", "Sil", "X");
            M("gizle", "Gizle", "H");
            M("golgeleme", "Gölgeleme menüsü", "Z");
            M("sec-araci", "Seçim aracı", "W");
            M("adlandir", "Yeniden adlandır", "F2");
        }
    }
}
