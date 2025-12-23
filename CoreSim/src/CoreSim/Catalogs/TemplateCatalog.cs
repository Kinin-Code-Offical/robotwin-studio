using System.Collections.Generic;
using CoreSim.Specs;

namespace CoreSim.Catalogs
{
    public class TemplateCatalog
    {
        public List<TemplateSpec> Templates { get; set; } = new List<TemplateSpec>();

        public TemplateSpec? Find(string id)
        {
            return Templates.Find(t => t.ID == id);
        }

        public void Register(TemplateSpec template)
        {
            if (Find(template.ID) == null)
            {
                Templates.Add(template);
            }
        }
    }
}
