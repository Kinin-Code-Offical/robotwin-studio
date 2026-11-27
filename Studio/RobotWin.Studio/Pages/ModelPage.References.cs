using System.Windows.Controls;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Pages
{
    /// <summary>[refgeom] A reference plane, axis or point in the inspector: its drawn size and what it is used for.</summary>
    public partial class ModelPage
    {
        private void ReferenceSection(ModelObject o)
        {
            var r = o.Reference!;
            Section("Referans", r.Kind == ReferenceKind.Point ? null : "çizim boyu");
            if (r.Kind != ReferenceKind.Point)
            {
                var host = new StackPanel();
                Props.Children.Add(host);
                Num(host, r.Kind == ReferenceKind.Plane ? "Kenar" : "Uzunluk", r.Size, "mm", v => r.Size = v, "Referans boyu", false, 5, 1, 1, 1e5);
            }

            Caption(r.Kind switch
            {
                ReferenceKind.Plane => "Düzlem nesnenin kendi XY düzlemidir, normali Z ekseni. Seçiliyken sağ tık ▸ Ayna kopyası parçaları ona göre aynalar; Ekle ▸ Referans düzlem ondan uzaklıkta yeni bir düzlem, iki düzlem seçiliyse ortalarındakini, Referans eksen ise kesiştikleri yeri verir.",
                ReferenceKind.Axis => "Eksen nesnenin kendi Z ekseni boyuncadır. Seçiliyken Dairesel dizi kopyaları onun çevresinde dağıtır; Ekle ▸ Referans düzlem ona dik bir düzlem verir.",
                _ => "Nokta nesnenin orijinidir. İki nokta seçip Referans eksen, üç nokta seçip Referans düzlem eklenebilir."
            });
        }
    }
}
