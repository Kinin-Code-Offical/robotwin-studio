using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Modeling
{
    public enum EditorMode
    {
        Object,
        Edit
    }

    public enum ElementMode
    {
        Vertex,
        Edge,
        Face
    }

    public enum ShadingMode
    {
        Solid,
        Wireframe,
        XRay
    }

    public enum ModelTool
    {
        Select,
        Move,
        Rotate,
        Scale,
        Measure
    }

    [Flags]
    public enum ModelChange
    {
        None = 0,
        Geometry = 1,
        Selection = 2,
        Mode = 4,
        View = 8,
        All = 15
    }

    /// <summary>
    /// State of the modelling workspace: the document, object and element selection,
    /// edit mode and undo/redo (whole-document snapshots, so every operation is undoable).
    /// </summary>
    public sealed class ModelEditor
    {
        private sealed class Snapshot
        {
            public string Json = string.Empty;
            public List<string> Selected = new List<string>();
            public string? Active;
            public EditorMode Mode;
            public ElementMode Element;
            public int[] Verts = Array.Empty<int>();
            public int[] Faces = Array.Empty<int>();
        }

        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();
        private Snapshot? _pending;
        private const int UndoLimit = 120;

        public ModelEditor()
        {
            Doc = new ModelDocument();
        }

        public ModelDocument Doc { get; private set; }
        public string? FilePath { get; set; }
        public bool IsDirty { get; private set; }

        public List<string> Selected { get; } = new List<string>();
        public string? ActiveId { get; private set; }
        public ModelObject? Active => Doc.Find(ActiveId);

        public EditorMode Mode { get; private set; } = EditorMode.Object;
        public ElementMode Element { get; private set; } = ElementMode.Vertex;
        public ShadingMode Shading { get; set; } = ShadingMode.Solid;
        public bool WireOverlay { get; set; }
        public bool SnapOn { get; set; }
        public ModelTool Tool { get; set; } = ModelTool.Select;

        /// <summary>Working mesh of the object in edit mode (object-local millimetres).</summary>
        public PolyMesh? EditMesh { get; private set; }

        /// <summary>Selected vertices (vertex/edge modes).</summary>
        public HashSet<int> SelVerts { get; } = new HashSet<int>();

        /// <summary>Selected faces (face mode).</summary>
        public HashSet<int> SelFaces { get; } = new HashSet<int>();

        public event Action<ModelChange>? Changed;
        public event Action<string>? StatusChanged;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void Status(string text) => StatusChanged?.Invoke(text);

        public void Notify(ModelChange change) => Changed?.Invoke(change);

        public IEnumerable<ModelObject> SelectedObjects => Selected.Select(Doc.Find).Where(o => o != null).Cast<ModelObject>();

        /// <summary>Selected objects without the ones whose parent is selected too (they move with it).</summary>
        public List<ModelObject> SelectedRoots()
        {
            var set = new HashSet<string>(Selected);
            var result = new List<ModelObject>();
            foreach (var o in SelectedObjects)
            {
                var p = Doc.Find(o.ParentId);
                bool under = false;
                var guard = new HashSet<string>();
                while (p != null && guard.Add(p.Id))
                {
                    if (set.Contains(p.Id))
                    {
                        under = true;
                        break;
                    }

                    p = Doc.Find(p.ParentId);
                }

                if (!under) result.Add(o);
            }

            return result;
        }

        // ================================================================ document

        public void Load(ModelDocument doc, string? path)
        {
            if (Mode == EditorMode.Edit) ExitEditInternal(false);
            doc.EnsureUid(path);   // [assembly]
            Doc = doc;
            FilePath = path;
            IsDirty = false;
            _undo.Clear();
            _redo.Clear();
            Selected.Clear();
            ActiveId = null;
            Mode = EditorMode.Object;
            Changed?.Invoke(ModelChange.All);
        }

        public void MarkSaved(string path)
        {
            FilePath = path;
            IsDirty = false;
            Changed?.Invoke(ModelChange.None);
        }

        // ================================================================ undo

        private Snapshot Capture() => new Snapshot
        {
            Json = Doc.ToJson(),
            Selected = new List<string>(Selected),
            Active = ActiveId,
            Mode = Mode,
            Element = Element,
            Verts = SelVerts.ToArray(),
            Faces = SelFaces.ToArray()
        };

        /// <summary>Takes the undo snapshot before an interactive change.</summary>
        public void BeginChange()
        {
            if (_pending != null) return;
            if (Mode == EditorMode.Edit) FlushEditMesh();
            _pending = Capture();
        }

        public void CommitChange(string status, ModelChange change = ModelChange.Geometry)
        {
            if (Mode == EditorMode.Edit) FlushEditMesh();
            if (_pending != null)
            {
                _undo.Add(_pending);
                if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
                _redo.Clear();
                _pending = null;
            }

            IsDirty = true;
            Status(status);
            Changed?.Invoke(change | ModelChange.Selection);
        }

        /// <summary>Restores the state captured by <see cref="BeginChange"/>.</summary>
        public void CancelChange()
        {
            if (_pending == null) return;
            Restore(_pending);
            _pending = null;
            Changed?.Invoke(ModelChange.All);
        }

        public bool InChange => _pending != null;

        /// <summary>Runs an operation as one undo step.</summary>
        public void Do(string status, Action change, ModelChange kind = ModelChange.Geometry)
        {
            BeginChange();
            try
            {
                change();
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is StackOverflowException))
            {
                CancelChange();
                Status("İşlem yapılamadı: " + ex.Message);
                return;
            }

            CommitChange(status, kind);
        }

        public void Undo()
        {
            if (_pending != null) CancelChange();
            if (_undo.Count == 0)
            {
                Status("Geri alınacak işlem yok");
                return;
            }

            if (Mode == EditorMode.Edit) FlushEditMesh();
            _redo.Add(Capture());
            var s = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Restore(s);
            IsDirty = true;
            Status("Geri alındı");
            Changed?.Invoke(ModelChange.All);
        }

        public void Redo()
        {
            if (_redo.Count == 0)
            {
                Status("Yinelenecek işlem yok");
                return;
            }

            if (Mode == EditorMode.Edit) FlushEditMesh();
            _undo.Add(Capture());
            var s = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Restore(s);
            IsDirty = true;
            Status("Yinelendi");
            Changed?.Invoke(ModelChange.All);
        }

        private void Restore(Snapshot s)
        {
            var doc = ModelDocument.FromJson(s.Json);
            Doc.Title = doc.Title;
            Doc.Description = doc.Description;
            Doc.GridMm = doc.GridMm;
            Doc.Objects = doc.Objects;
            Selected.Clear();
            Selected.AddRange(s.Selected.Where(id => Doc.Find(id) != null));
            ActiveId = Doc.Find(s.Active) != null ? s.Active : Selected.LastOrDefault();
            Element = s.Element;
            SelVerts.Clear();
            SelFaces.Clear();
            if (s.Mode == EditorMode.Edit && Active != null)
            {
                Mode = EditorMode.Edit;
                EditMesh = Active.LocalMesh().Clone();
                SelVerts.UnionWith(s.Verts.Where(v => v < EditMesh.Vertices.Count));
                SelFaces.UnionWith(s.Faces.Where(f => f < EditMesh.Faces.Count));
            }
            else
            {
                Mode = EditorMode.Object;
                EditMesh = null;
            }
        }

        // ================================================================ object selection

        public void Select(string? id, bool extend = false)
        {
            if (!extend) Selected.Clear();
            if (id != null)
            {
                if (extend && Selected.Contains(id) && ActiveId == id) Selected.Remove(id);
                else
                {
                    Selected.Remove(id);
                    Selected.Add(id);
                }
            }

            ActiveId = id != null && Selected.Contains(id) ? id : Selected.LastOrDefault();
            Changed?.Invoke(ModelChange.Selection);
        }

        public void SelectMany(IEnumerable<string> ids, bool extend, bool subtract = false)
        {
            if (!extend && !subtract) Selected.Clear();
            foreach (var id in ids)
            {
                if (subtract) Selected.Remove(id);
                else if (!Selected.Contains(id)) Selected.Add(id);
            }

            if (ActiveId == null || !Selected.Contains(ActiveId)) ActiveId = Selected.LastOrDefault();
            Changed?.Invoke(ModelChange.Selection);
        }

        public void SelectAll()
        {
            if (Mode == EditorMode.Edit && EditMesh != null)
            {
                if (Element == ElementMode.Face)
                {
                    SelFaces.Clear();
                    for (int f = 0; f < EditMesh.Faces.Count; f++) SelFaces.Add(f);
                }
                else
                {
                    SelVerts.Clear();
                    for (int v = 0; v < EditMesh.Vertices.Count; v++) SelVerts.Add(v);
                }
            }
            else
            {
                Selected.Clear();
                Selected.AddRange(Doc.Objects.Where(o => o.Visible).Select(o => o.Id));
                if (ActiveId == null || !Selected.Contains(ActiveId)) ActiveId = Selected.LastOrDefault();
            }

            Changed?.Invoke(ModelChange.Selection);
        }

        public void DeselectAll()
        {
            if (Mode == EditorMode.Edit)
            {
                SelVerts.Clear();
                SelFaces.Clear();
            }
            else
            {
                Selected.Clear();
            }

            Changed?.Invoke(ModelChange.Selection);
        }

        public void InvertSelection()
        {
            if (Mode == EditorMode.Edit && EditMesh != null)
            {
                if (Element == ElementMode.Face)
                {
                    var all = Enumerable.Range(0, EditMesh.Faces.Count).Where(f => !SelFaces.Contains(f)).ToList();
                    SelFaces.Clear();
                    SelFaces.UnionWith(all);
                }
                else
                {
                    var all = Enumerable.Range(0, EditMesh.Vertices.Count).Where(v => !SelVerts.Contains(v)).ToList();
                    SelVerts.Clear();
                    SelVerts.UnionWith(all);
                }
            }
            else
            {
                var ids = Doc.Objects.Where(o => o.Visible && !Selected.Contains(o.Id)).Select(o => o.Id).ToList();
                Selected.Clear();
                Selected.AddRange(ids);
                ActiveId = Selected.LastOrDefault();
            }

            Changed?.Invoke(ModelChange.Selection);
        }

        // ================================================================ edit mode

        /// <summary>Tab: toggles edit mode of the active object (primitives are converted to meshes).</summary>
        public void ToggleEditMode()
        {
            if (Mode == EditorMode.Edit)
            {
                ExitEditInternal(true);
                Status("Nesne modu");
                Changed?.Invoke(ModelChange.All);
                return;
            }

            var o = Active;
            if (o == null)
            {
                Status("Düzenlemek için bir nesne seçin");
                return;
            }

            if (o.Primitive != null)
            {
                Do($"'{o.Name}' düzenlenebilir ağa dönüştürüldü", () => ModelOps.ConvertToMesh(o));
            }

            o.Visible = true;
            Mode = EditorMode.Edit;
            EditMesh = o.LocalMesh().Clone();
            SelVerts.Clear();
            SelFaces.Clear();
            Selected.Clear();
            Selected.Add(o.Id);
            Status($"Düzenleme modu: {o.Name} · 1/2/3 nokta/kenar/yüz seçimi · E çıkar · I içe kaydır · Tab çıkış");
            Changed?.Invoke(ModelChange.All);
        }

        private void ExitEditInternal(bool flush)
        {
            if (flush) FlushEditMesh();
            Mode = EditorMode.Object;
            EditMesh = null;
            SelVerts.Clear();
            SelFaces.Clear();
        }

        /// <summary>Writes the working mesh back into the document.</summary>
        public void FlushEditMesh()
        {
            if (Mode != EditorMode.Edit || EditMesh == null || Active == null) return;
            Active.SetMesh(EditMesh.Clone());
        }

        public void SetElementMode(ElementMode mode)
        {
            if (Element == mode) return;
            if (EditMesh != null)
            {
                if (mode == ElementMode.Face && Element != ElementMode.Face)
                {
                    SelFaces.Clear();
                    SelFaces.UnionWith(MeshEdit.FacesOfVertices(EditMesh, SelVerts));
                }
                else if (mode != ElementMode.Face && Element == ElementMode.Face)
                {
                    SelVerts.Clear();
                    SelVerts.UnionWith(MeshEdit.VerticesOfFaces(EditMesh, SelFaces));
                }
            }

            Element = mode;
            Changed?.Invoke(ModelChange.Selection | ModelChange.Mode);
        }

        /// <summary>Vertices affected by the element selection.</summary>
        public HashSet<int> SelectedVertexSet()
        {
            if (EditMesh == null) return new HashSet<int>();
            return Element == ElementMode.Face ? MeshEdit.VerticesOfFaces(EditMesh, SelFaces) : new HashSet<int>(SelVerts);
        }

        /// <summary>Faces affected by the element selection (fully selected faces in vertex/edge modes).</summary>
        public List<int> SelectedFaceList()
        {
            if (EditMesh == null) return new List<int>();
            return Element == ElementMode.Face ? SelFaces.ToList() : MeshEdit.FacesOfVertices(EditMesh, SelVerts);
        }

        public List<(int A, int B)> SelectedEdges()
        {
            if (EditMesh == null) return new List<(int, int)>();
            var vs = SelectedVertexSet();
            return EditMesh.Edges().Where(e => vs.Contains(e.A) && vs.Contains(e.B)).ToList();
        }

        /// <summary>Replaces the element selection with a vertex set (faces fully inside it in face mode).</summary>
        private void SetVertexSelection(HashSet<int> verts, bool extend = false)
        {
            if (EditMesh == null) return;
            if (!extend)
            {
                SelVerts.Clear();
                SelFaces.Clear();
            }

            if (Element == ElementMode.Face) SelFaces.UnionWith(MeshEdit.FacesOfVertices(EditMesh, verts));
            else SelVerts.UnionWith(verts);
            Changed?.Invoke(ModelChange.Selection);
        }

        /// <summary>L / Ctrl+L: the whole connected piece of the selection.</summary>
        public void SelectLinked()
        {
            if (EditMesh == null) return;
            var seeds = SelectedVertexSet();
            if (seeds.Count == 0)
            {
                Status("Bağlı parçayı seçmek için önce bir nokta, kenar ya da yüz seçin");
                return;
            }

            SetVertexSelection(MeshSelect.Linked(EditMesh, seeds));
        }

        /// <summary>Ctrl+Numpad + / −: grows or shrinks the selection by one ring of faces.</summary>
        public void GrowSelection(bool grow)
        {
            if (EditMesh == null) return;
            var vs = SelectedVertexSet();
            if (vs.Count == 0) return;
            SetVertexSelection(grow ? MeshSelect.Grow(EditMesh, vs) : MeshSelect.Shrink(EditMesh, vs));
        }

        /// <summary>Alt+click: the edge loop through (a, b); in face mode the face loop across it.</summary>
        public void SelectLoop(int a, int b, bool extend)
        {
            if (EditMesh == null) return;
            if (Element == ElementMode.Face)
            {
                if (!extend)
                {
                    SelVerts.Clear();
                    SelFaces.Clear();
                }

                SelFaces.UnionWith(MeshSelect.FaceLoop(EditMesh, a, b));
                Changed?.Invoke(ModelChange.Selection);
                return;
            }

            SetVertexSelection(new HashSet<int>(MeshSelect.EdgeLoop(EditMesh, a, b)), extend);
        }

        public bool HasElementSelection => Element == ElementMode.Face ? SelFaces.Count > 0 : SelVerts.Count > 0;

        public void SelectElements(IEnumerable<int> verts, IEnumerable<int> faces, bool extend, bool subtract = false)
        {
            if (!extend && !subtract)
            {
                SelVerts.Clear();
                SelFaces.Clear();
            }

            if (Element == ElementMode.Face)
            {
                foreach (int f in faces)
                {
                    if (subtract) SelFaces.Remove(f);
                    else SelFaces.Add(f);
                }
            }
            else
            {
                foreach (int v in verts)
                {
                    if (subtract) SelVerts.Remove(v);
                    else SelVerts.Add(v);
                }
            }

            Changed?.Invoke(ModelChange.Selection);
        }

        /// <summary>Toggles one element (Shift+click).</summary>
        public void ToggleElement(IReadOnlyCollection<int> verts, int face)
        {
            if (Element == ElementMode.Face)
            {
                if (face < 0) return;
                if (!SelFaces.Remove(face)) SelFaces.Add(face);
            }
            else
            {
                bool all = verts.All(SelVerts.Contains);
                foreach (int v in verts)
                {
                    if (all) SelVerts.Remove(v);
                    else SelVerts.Add(v);
                }
            }

            Changed?.Invoke(ModelChange.Selection);
        }

        /// <summary>Applies the result of a topology operation as the new selection.</summary>
        public void SelectResult(EditResult r)
        {
            SelVerts.Clear();
            SelFaces.Clear();
            if (Element == ElementMode.Face) SelFaces.UnionWith(r.Faces);
            else SelVerts.UnionWith(r.Vertices);
        }

        /// <summary>Replaces the working mesh (used by modal operators that re-run an operation from a copy).</summary>
        public void SetEditMesh(PolyMesh mesh)
        {
            EditMesh = mesh;
        }

        // ================================================================ object operations

        public static bool IsMeshLike(ModelObject o) => o.Primitive != null || o.Mesh != null;

        public ModelObject AddPrimitive(PrimitiveKind kind, Vec3 at)
        {
            ModelObject? added = null;
            Do(PrimitiveSchema.Name(kind) + " eklendi", () =>
            {
                if (Mode == EditorMode.Edit) ExitEditInternal(true);
                added = ModelOps.AddPrimitive(Doc, kind, at.X, at.Y, at.Z);
                Selected.Clear();
                Selected.Add(added.Id);
                ActiveId = added.Id;
            }, ModelChange.All);
            return added!;
        }

        public List<ModelObject> AddPart(string templateId, Vec3 at)
        {
            var added = new List<ModelObject>();
            Do((RobotParts.Find(templateId)?.Name ?? "Parça") + " eklendi", () =>
            {
                if (Mode == EditorMode.Edit) ExitEditInternal(true);
                added = RobotParts.Insert(Doc, templateId, at.X, at.Y);
                Selected.Clear();
                Selected.AddRange(added.Select(o => o.Id));
                ActiveId = added[0].Id;
            }, ModelChange.All);
            return added;
        }

        public void AddMeshObject(PolyMesh mesh, string name)
        {
            Do(name + " içe aktarıldı", () =>
            {
                if (Mode == EditorMode.Edit) ExitEditInternal(true);
                var o = ModelOps.AddMesh(Doc, mesh, name);
                Selected.Clear();
                Selected.Add(o.Id);
                ActiveId = o.Id;
            }, ModelChange.All);
        }

        public void DeleteSelected()
        {
            var objs = SelectedObjects.ToList();
            if (objs.Count == 0) return;
            Do(objs.Count == 1 ? $"'{objs[0].Name}' silindi" : $"{objs.Count} nesne silindi", () =>
            {
                foreach (var o in objs) Doc.Remove(o);
                Selected.Clear();
                ActiveId = null;
            }, ModelChange.All);
        }

        /// <summary>Copies the selection; the copies become selected (Shift+D then moves them).</summary>
        public bool DuplicateSelected()
        {
            var objs = SelectedObjects.ToList();
            if (objs.Count == 0) return false;
            BeginChange();
            var copies = ModelOps.Duplicate(Doc, objs);
            Selected.Clear();
            Selected.AddRange(copies.Select(c => c.Id));
            ActiveId = copies.Last().Id;
            Changed?.Invoke(ModelChange.All);
            return true;
        }

        public void Join()
        {
            var active = Active;
            var objs = SelectedObjects.ToList();
            if (active == null || objs.Count < 2)
            {
                Status("Birleştirmek için en az iki nesne seçin (son seçilen etkin nesne olur)");
                return;
            }

            Do($"{objs.Count} nesne '{active.Name}' içinde birleştirildi", () =>
            {
                ModelOps.Join(Doc, active, objs);
                Selected.Clear();
                Selected.Add(active.Id);
            }, ModelChange.All);
        }

        public void Boolean(BooleanOp op)
        {
            var active = Active;
            var tools = SelectedObjects.Where(o => o != active).ToList();
            if (active == null || tools.Count == 0)
            {
                Status("Boolean için iki nesne seçin: önce araç(lar), en son hedef (etkin) nesne");
                return;
            }

            string name = op == BooleanOp.Union ? "Birleşim" : op == BooleanOp.Difference ? "Fark" : "Kesişim";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Do($"{name}: '{active.Name}' ({tools.Count} araç)", () =>
            {
                ModelOps.Boolean(Doc, active, tools, op);
                Selected.Clear();
                Selected.Add(active.Id);
            }, ModelChange.All);
            var topo = active.LocalMesh().Topology();
            Status($"{name} tamam · {active.LocalMesh().Faces.Count} yüz · {(topo.IsClosed ? "kapalı (su geçirmez)" : "açık ağ: " + topo)} · {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>Cuts <paramref name="target"/> in two closed pieces along a world plane; both pieces end up selected.</summary>
        public ModelObject? Slice(ModelObject target, Vec3 point, Vec3 normal)
        {
            ModelObject? piece = null;
            Do($"'{target.Name}' dilimlendi", () =>
            {
                piece = ModelOps.Slice(Doc, target, point, normal);
                if (piece == null) return;
                Selected.Clear();
                Selected.Add(target.Id);
                Selected.Add(piece.Id);
                ActiveId = piece.Id;
            }, ModelChange.All);
            Status(piece == null ? "Çizgi nesneyi kesmiyor" : $"'{target.Name}' iki kapalı parçaya ayrıldı · bir parçayı seçip G ile taşıyın, R ile döndürün");
            return piece;
        }

        public void MirrorCopy(Axis axis)
        {
            var objs = SelectedRoots();
            if (objs.Count == 0) return;
            Do($"{axis} eksenine göre ayna kopyası", () =>
            {
                var copies = ModelOps.MirrorCopy(Doc, objs, axis, Vec3.Zero);
                Selected.Clear();
                Selected.AddRange(copies.Select(c => c.Id));
                ActiveId = copies.Last().Id;
            }, ModelChange.All);
        }

        public void MirrorInPlace(Axis axis)
        {
            var objs = SelectedRoots();
            if (objs.Count == 0) return;
            var pivot = ModelOps.WorldBounds(Doc, objs).Center;
            Do($"{axis} ekseninde aynalandı", () => ModelOps.MirrorInPlace(Doc, objs, axis, pivot), ModelChange.All);
        }

        public void ArrayLinear(int count, Vec3 offset)
        {
            var objs = SelectedRoots();
            if (objs.Count == 0 || count < 2) return;
            Do($"Doğrusal dizi: {count} adet", () =>
            {
                var copies = ModelOps.ArrayLinear(Doc, objs, count, offset);
                Selected.AddRange(copies.Select(c => c.Id));
            }, ModelChange.All);
        }

        public void ArrayCircular(int count, double degrees, Axis axis, Vec3 center)
        {
            var objs = SelectedRoots();
            if (objs.Count == 0 || count < 2) return;
            Do($"Dairesel dizi: {count} adet", () =>
            {
                var copies = ModelOps.ArrayCircular(Doc, objs, count, degrees, axis, center);
                Selected.AddRange(copies.Select(c => c.Id));
            }, ModelChange.All);
        }

        public void ConvertToMesh()
        {
            var objs = SelectedObjects.Where(o => o.Primitive != null).ToList();
            if (objs.Count == 0) return;
            Do("Ağa dönüştürüldü", () =>
            {
                foreach (var o in objs) ModelOps.ConvertToMesh(o);
            });
        }

        public void ApplyTransform()
        {
            var objs = SelectedObjects.ToList();
            if (objs.Count == 0) return;
            Do("Döndürme ve ölçek uygulandı", () =>
            {
                foreach (var o in objs) ModelOps.ApplyTransform(Doc, o);
            });
        }

        public void OriginToGeometry()
        {
            var objs = SelectedObjects.ToList();
            if (objs.Count == 0) return;
            Do("Orijin geometriye taşındı", () =>
            {
                foreach (var o in objs) ModelOps.OriginToGeometry(Doc, o);
            });
        }

        public void SetSmooth(bool smooth)
        {
            var objs = SelectedObjects.ToList();
            if (objs.Count == 0) return;
            Do(smooth ? "Yumuşak gölgelendirme" : "Düz gölgelendirme", () =>
            {
                foreach (var o in objs) o.SmoothShading = smooth;
            });
        }

        public void HideSelected(bool unselected = false)
        {
            var targets = unselected ? Doc.Objects.Where(o => !Selected.Contains(o.Id)).ToList() : SelectedObjects.ToList();
            if (targets.Count == 0) return;
            Do("Gizlendi", () =>
            {
                foreach (var o in targets) o.Visible = false;
                if (!unselected) Selected.Clear();
            }, ModelChange.All);
        }

        public void UnhideAll()
        {
            var hidden = Doc.Objects.Where(o => !o.Visible).ToList();
            if (hidden.Count == 0) return;
            Do("Hepsi gösterildi", () =>
            {
                foreach (var o in hidden) o.Visible = true;
                Selected.Clear();
                Selected.AddRange(hidden.Select(o => o.Id));
                ActiveId = Selected.LastOrDefault();
            }, ModelChange.All);
        }

        public void SetVisible(ModelObject o, bool visible) =>
            Do(visible ? $"'{o.Name}' gösterildi" : $"'{o.Name}' gizlendi", () =>
            {
                o.Visible = visible;
                if (!visible) Selected.Remove(o.Id);
            }, ModelChange.All);

        /// <summary>Ctrl+P: parents the selected objects to the active one (keeping their placement).</summary>
        public void ParentToActive()
        {
            var parent = Active;
            var children = SelectedObjects.Where(o => o != parent).ToList();
            if (parent == null || children.Count == 0)
            {
                Status("Ebeveyn için önce çocukları, en son ebeveyni seçin");
                return;
            }

            Do($"{children.Count} nesne '{parent.Name}' altına bağlandı", () =>
            {
                foreach (var c in children)
                {
                    // refuse cycles
                    var p = parent;
                    bool cycle = false;
                    var guard = new HashSet<string>();
                    while (p != null && guard.Add(p.Id))
                    {
                        if (p.Id == c.Id) cycle = true;
                        p = Doc.Find(p.ParentId);
                    }

                    if (cycle) continue;
                    var world = Doc.WorldMatrix(c);
                    c.ParentId = parent.Id;
                    ModelOps.SetWorldMatrix(Doc, c, world);
                }
            }, ModelChange.All);
        }

        public void ClearParent()
        {
            var objs = SelectedObjects.Where(o => o.ParentId != null).ToList();
            if (objs.Count == 0) return;
            Do("Ebeveyn bağı kaldırıldı", () =>
            {
                foreach (var c in objs)
                {
                    var world = Doc.WorldMatrix(c);
                    c.ParentId = null;
                    ModelOps.SetWorldMatrix(Doc, c, world);
                }
            }, ModelChange.All);
        }

        // ================================================================ edit-mode operations

        private bool RequireEdit()
        {
            if (Mode == EditorMode.Edit && EditMesh != null) return true;
            Status("Bu işlem düzenleme modunda yapılır (Tab)");
            return false;
        }

        public void DeleteElements()
        {
            if (!RequireEdit()) return;
            var mesh = EditMesh!;
            if (!HasElementSelection) return;
            Do("Silindi", () =>
            {
                switch (Element)
                {
                    case ElementMode.Face:
                        MeshEdit.DeleteFaces(mesh, SelFaces);
                        break;
                    case ElementMode.Edge:
                        MeshEdit.DeleteEdges(mesh, SelectedEdges());
                        break;
                    default:
                        MeshEdit.DeleteVertices(mesh, SelVerts);
                        break;
                }

                SelVerts.Clear();
                SelFaces.Clear();
            });
        }

        public void MergeAtCenter()
        {
            if (!RequireEdit()) return;
            var verts = SelectedVertexSet();
            if (verts.Count < 2)
            {
                Status("Birleştirmek için en az iki nokta seçin");
                return;
            }

            Do($"{verts.Count} nokta merkezde birleştirildi", () =>
            {
                int v = MeshEdit.MergeVertices(EditMesh!, verts);
                SelVerts.Clear();
                SelFaces.Clear();
                if (v >= 0) SelVerts.Add(v);
            });
        }

        public void MergeByDistance(double distance)
        {
            if (!RequireEdit()) return;
            int removed = 0;
            Do("Mesafeye göre birleştirildi", () =>
            {
                removed = MeshEdit.MergeByDistance(EditMesh!, distance);
                SelVerts.Clear();
                SelFaces.Clear();
            });
            Status($"Mesafeye göre birleştir: {removed} nokta kaldırıldı");
        }

        public void Subdivide()
        {
            if (!RequireEdit()) return;
            var faces = SelectedFaceList();
            Do("Alt bölümlere ayrıldı", () =>
            {
                var r = MeshEdit.Subdivide(EditMesh!, faces.Count > 0 ? faces : null);
                SelectResult(r);
            });
        }

        public void SmoothSubdivide()
        {
            if (!RequireEdit()) return;
            Do("Catmull–Clark yumuşatma", () =>
            {
                SetEditMesh(MeshEdit.CatmullClark(EditMesh!));
                SelVerts.Clear();
                SelFaces.Clear();
            });
        }

        public void RecalculateNormals()
        {
            if (!RequireEdit()) return;
            int n = 0;
            Do("Normaller yeniden hesaplandı", () => n = MeshEdit.RecalculateNormals(EditMesh!));
            Status($"Normaller dışa döndürüldü ({n} yüz çevrildi)");
        }

        public void FlipNormals()
        {
            if (!RequireEdit()) return;
            var faces = SelectedFaceList();
            if (faces.Count == 0) return;
            Do("Normaller çevrildi", () => MeshEdit.FlipFaces(EditMesh!, faces));
        }

        public void Fill()
        {
            if (!RequireEdit()) return;
            var verts = SelectedVertexSet();
            if (verts.Count < 3)
            {
                Status("Yüz oluşturmak için en az üç nokta seçin");
                return;
            }

            Do("Yüz oluşturuldu", () =>
            {
                int f = MeshEdit.Fill(EditMesh!, verts);
                if (f >= 0 && Element == ElementMode.Face)
                {
                    SelFaces.Clear();
                    SelFaces.Add(f);
                }
            });
        }

        public void TriangulateFaces()
        {
            if (!RequireEdit()) return;
            var faces = SelectedFaceList();
            if (faces.Count == 0) return;
            Do("Üçgenlere bölündü", () =>
            {
                MeshEdit.TriangulateFaces(EditMesh!, faces);
                SelFaces.Clear();
            });
        }

        public void Separate()
        {
            if (!RequireEdit()) return;
            var faces = SelectedFaceList();
            if (faces.Count == 0)
            {
                Status("Ayırmak için yüz seçin");
                return;
            }

            var o = Active!;
            Do("Seçim ayrı nesne yapıldı", () =>
            {
                Active!.SetMesh(EditMesh!.Clone());
                var part = ModelOps.Separate(Doc, o, faces);
                EditMesh = o.LocalMesh().Clone();
                SelVerts.Clear();
                SelFaces.Clear();
                if (part != null) Status($"'{part.Name}' oluşturuldu");
            }, ModelChange.All);
        }
    }
}
