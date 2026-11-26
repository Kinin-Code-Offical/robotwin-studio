using System;
using System.Linq;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [components] Where a component card's ? opens the guide: a word in the card's title picks the World guide's
    /// section about it (Turkish or English page); a card without its own section searches the guide for its name.
    /// No WPF here, so a test checks every heading still exists in both guides.
    /// </summary>
    public static class CardHelp
    {
        /// <summary>A word in a card's title → the guide's heading about it (Turkish, English).</summary>
        public static readonly (string Key, string Tr, string En)[] Topics =
        {
            ("Konum ve yön", "Taşı, döndür, ölçekle tutamaçları", "Move, rotate and scale handles"),
            ("Boş nesne", "Sahne ağacı", "Scene tree"),
            ("Düzenleyici", "Sahne ağacı", "Scene tree"),
            ("Kinematik", "Kol kinematiği: ileri ve ters kinematik", "Arm kinematics: forward and inverse"),
            ("PID", "PID ayarlayıcı: motorun hız döngüsü", "PID tuner: a motor's speed loop"),
            ("Hız kontrolü", "Tekerlekli robotun kinematiği ve odometri", "Kinematics and odometry of a wheeled robot"),
            ("Araç", "Tekerlekli robotun kinematiği ve odometri", "Kinematics and odometry of a wheeled robot"),
            ("Basınçlı kap", "Basınçlı kaplar ve roketler", "Pressure vessels and rockets"),
            ("Lüle", "Basınçlı kaplar ve roketler", "Pressure vessels and rockets"),
            ("Katı yakıt", "Basınçlı kaplar ve roketler", "Pressure vessels and rockets"),
            ("paraşüt", "Paraşüt", "Parachute"),
            ("Arıza", "Arıza senaryoları", "Fault scenarios"),
            ("Mekanik bağlantılar", "Mekanik bağlantılar", "Mechanical joints"),
            ("Güç aktarımı", "Güç aktarım parçaları: gerçekteki gibi kurulan mekanizma", "Power transmission parts: a mechanism built as in reality"),
            ("Kablolar", "Kablolar", "Cables"),
            ("Kablosuz", "Kumandalar, radyo ve USB", "Controllers, radio and USB"),
            ("Kumanda", "Kumandalar, radyo ve USB", "Controllers, radio and USB"),
            ("Fotoelektrik", "Fotoelektrik sensör ve sınır anahtarı", "Photoelectric sensor and limit switch"),
            ("Kompresör", "Kompresör ve hava tankı", "Compressor and air tank"),
            ("Fizik", "Fizik ve ortam", "Physics and environment"),
            ("Görünüş ve yüzey", "Fizik ve ortam", "Physics and environment"),
            ("Hava", "Fizik ve ortam", "Physics and environment"),
        };

        /// <summary>The guide page and heading for a card's title, or null when it has no section of its own.</summary>
        public static (string Page, string Heading)? Topic(string title, bool english)
        {
            var topic = Topics.FirstOrDefault(t => title.IndexOf(t.Key, StringComparison.CurrentCultureIgnoreCase) >= 0);
            if (topic.Key == null) return null;
            return english ? ("07-world.md", topic.En) : ("07-dunya.md", topic.Tr);
        }

        /// <summary>The help section to open: "page.md#Heading", or "search:" and the (shown) title.</summary>
        public static string Section(string title, bool english, Func<string, string> shown) =>
            Topic(title, english) is { } t ? t.Page + "#" + t.Heading : "search:" + shown(title);
    }
}
