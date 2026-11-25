// RobotWin.Occt.dll — RobotWin Studio's thin bridge to Open CASCADE Technology (OCCT) for CAD exchange files.
//
// Only the file formats go through OCCT: a STEP / IGES / BREP file is read into an XCAF document (shapes, assembly
// placements, colours), meshed with BRepMesh, and handed to RobotWin as triangles; a RobotWin triangle mesh is sewn
// into a solid and written as STEP. Modelling, physics and drawing stay in RobotWin's own engines.
//
// Plain C entry points (P/Invoke from RobotTwin.CoreSim.Formats.OcctBridge). Lengths are millimetres.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <cmath>
#include <cstring>
#include <string>
#include <vector>

#include <BRepBndLib.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepTools.hxx>
#include <BRep_Tool.hxx>
#include <BRep_Builder.hxx>
#include <Bnd_Box.hxx>
#include <IGESCAFControl_Reader.hxx>
#include <Interface_Static.hxx>
#include <Message.hxx>
#include <Message_Messenger.hxx>
#include <Message_Printer.hxx>
#include <RWMesh_FaceIterator.hxx>
#include <STEPCAFControl_Reader.hxx>
#include <STEPControl_Writer.hxx>
#include <Standard_Failure.hxx>
#include <Standard_Version.hxx>
#include <NCollection_Sequence.hxx>
#include <TDF_Label.hxx>
#include <TDocStd_Document.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Shell.hxx>
#include <XCAFApp_Application.hxx>
#include <XCAFDoc_DocumentTool.hxx>
#include <XCAFDoc_ShapeTool.hxx>
#include <XCAFPrs_DocumentExplorer.hxx>

#define RW_API extern "C" __declspec(dllexport)

namespace
{
struct Mesh
{
  std::vector<double>        xyz;   // 3 per vertex, mm
  std::vector<int>           tri;   // 3 per triangle, 0-based
  std::vector<unsigned char> rgb;   // 3 per triangle
  bool                       colored = false;
  int                        shapes  = 0;
};

std::string Utf8(const wchar_t* w)
{
  if (w == nullptr) return std::string();
  int n = WideCharToMultiByte(CP_UTF8, 0, w, -1, nullptr, 0, nullptr, nullptr);
  std::string s(n > 0 ? n - 1 : 0, '\0');
  if (n > 1) WideCharToMultiByte(CP_UTF8, 0, w, -1, &s[0], n, nullptr, nullptr);
  return s;
}

std::wstring Lower(const wchar_t* w)
{
  std::wstring s(w ? w : L"");
  for (auto& c : s) c = (wchar_t)towlower(c);
  return s;
}

bool EndsWith(const std::wstring& s, const wchar_t* tail)
{
  size_t n = wcslen(tail);
  return s.size() >= n && s.compare(s.size() - n, n, tail) == 0;
}

void Say(char* error, int cap, const std::string& text)
{
  if (error == nullptr || cap <= 0) return;
  size_t n = text.size() < (size_t)(cap - 1) ? text.size() : (size_t)(cap - 1);
  memcpy(error, text.data(), n);
  error[n] = 0;
}

// The chord deflection for a shape: a fine fraction of its size, so small connectors and large frames both look right.
double AutoDeflection(const TopoDS_Shape& shape)
{
  Bnd_Box box;
  BRepBndLib::Add(shape, box);
  if (box.IsVoid()) return 0.05;
  double x0, y0, z0, x1, y1, z1;
  box.Get(x0, y0, z0, x1, y1, z1);
  double diagonal = std::sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0) + (z1 - z0) * (z1 - z0));
  double d = diagonal * 0.0008;
  return d < 0.002 ? 0.002 : d > 1.0 ? 1.0 : d;
}

void MeshShapes(const NCollection_Sequence<TDF_Label>& roots, double linear, double angular)
{
  for (int i = 1; i <= roots.Length(); ++i)
  {
    TopoDS_Shape shape = XCAFDoc_ShapeTool::GetShape(roots.Value(i));
    if (shape.IsNull()) continue;
    double d = linear > 0 ? linear : AutoDeflection(shape);
    BRepMesh_IncrementalMesh mesher(shape, d, false, angular, true);
  }
}

// Every leaf of the document (assemblies expanded, with their placements and colours) into one triangle list.
void Collect(const occ::handle<TDocStd_Document>& doc, Mesh& out)
{
  XCAFPrs_DocumentExplorer explorer(doc, XCAFPrs_DocumentExplorerFlags_OnlyLeafNodes);
  for (; explorer.More(); explorer.Next())
  {
    const XCAFPrs_DocumentNode& node = explorer.Current();
    out.shapes++;
    for (RWMesh_FaceIterator face(node.RefLabel, node.Location, true, node.Style); face.More(); face.Next())
    {
      if (face.IsEmpty()) continue;
      int base = (int)(out.xyz.size() / 3);
      for (int n = face.NodeLower(); n <= face.NodeUpper(); ++n)
      {
        gp_Pnt p = face.NodeTransformed(n);
        out.xyz.push_back(p.X());
        out.xyz.push_back(p.Y());
        out.xyz.push_back(p.Z());
      }

      unsigned char r = 200, g = 200, b = 200;
      if (face.HasFaceColor())
      {
        const Quantity_Color& c = face.FaceColor().GetRGB();
        double cr, cg, cb;
        c.Values(cr, cg, cb, Quantity_TOC_sRGB);
        r = (unsigned char)std::lround(cr * 255.0);
        g = (unsigned char)std::lround(cg * 255.0);
        b = (unsigned char)std::lround(cb * 255.0);
        out.colored = true;
      }

      for (int t = face.ElemLower(); t <= face.ElemUpper(); ++t)
      {
        Poly_Triangle tri = face.TriangleOriented(t);
        int a, bb, c;
        tri.Get(a, bb, c);
        out.tri.push_back(base + a - face.NodeLower());
        out.tri.push_back(base + bb - face.NodeLower());
        out.tri.push_back(base + c - face.NodeLower());
        out.rgb.push_back(r);
        out.rgb.push_back(g);
        out.rgb.push_back(b);
      }
    }
  }
}

void Quiet()
{
  // OCCT's messages would go to stdout; the app reports what matters itself.
  static bool done = false;
  if (done) return;
  done = true;
  Message::DefaultMessenger()->RemovePrinters(STANDARD_TYPE(Message_Printer));
}
} // namespace

RW_API int rw_occt_version(char* buffer, int capacity)
{
  Say(buffer, capacity, OCC_VERSION_COMPLETE);
  return 0;
}

// Reads a STEP (.step/.stp), IGES (.iges/.igs) or BREP (.brep) file and meshes it. Returns a handle (free it with
// rw_occt_free) or null with the reason in error. linear <= 0: the deflection follows each shape's size.
RW_API void* rw_occt_read(const wchar_t* path, double linear, double angular, int* vertexCount, int* triangleCount,
                          int* colored, char* error, int errorCapacity)
{
  Quiet();
  try
  {
    std::wstring lower = Lower(path);
    std::string  file  = Utf8(path);
    occ::handle<TDocStd_Document> doc;
    XCAFApp_Application::GetApplication()->NewDocument("BinXCAF", doc);
    XCAFDoc_DocumentTool::SetLengthUnit(doc, 0.001);   // the document in millimetres: the readers convert into it
    if (EndsWith(lower, L".step") || EndsWith(lower, L".stp"))
    {
      STEPCAFControl_Reader reader;
      reader.SetColorMode(true);
      reader.SetNameMode(true);
      reader.SetLayerMode(false);
      if (reader.ReadFile(file.c_str()) != IFSelect_RetDone)
      {
        Say(error, errorCapacity, "STEP file could not be read");
        return nullptr;
      }

      if (!reader.Transfer(doc))
      {
        Say(error, errorCapacity, "STEP file has no shapes to transfer");
        return nullptr;
      }
    }
    else if (EndsWith(lower, L".iges") || EndsWith(lower, L".igs"))
    {
      IGESCAFControl_Reader reader;
      reader.SetColorMode(true);
      reader.SetNameMode(true);
      if (reader.ReadFile(file.c_str()) != IFSelect_RetDone)
      {
        Say(error, errorCapacity, "IGES file could not be read");
        return nullptr;
      }

      if (!reader.Transfer(doc))
      {
        Say(error, errorCapacity, "IGES file has no shapes to transfer");
        return nullptr;
      }
    }
    else if (EndsWith(lower, L".brep"))
    {
      TopoDS_Shape shape;
      BRep_Builder builder;
      if (!BRepTools::Read(shape, file.c_str(), builder) || shape.IsNull())
      {
        Say(error, errorCapacity, "BREP file could not be read");
        return nullptr;
      }

      XCAFDoc_DocumentTool::ShapeTool(doc->Main())->AddShape(shape);
    }
    else
    {
      Say(error, errorCapacity, "not a STEP, IGES or BREP file");
      return nullptr;
    }

    NCollection_Sequence<TDF_Label> roots;
    XCAFDoc_DocumentTool::ShapeTool(doc->Main())->GetFreeShapes(roots);
    MeshShapes(roots, linear, angular > 0 ? angular : 0.35);
    auto* mesh = new Mesh();
    Collect(doc, *mesh);
    doc->Close();
    if (mesh->tri.empty())
    {
      delete mesh;
      Say(error, errorCapacity, "the file holds no surfaces to mesh (wires or points only)");
      return nullptr;
    }

    *vertexCount   = (int)(mesh->xyz.size() / 3);
    *triangleCount = (int)(mesh->tri.size() / 3);
    *colored       = mesh->colored ? 1 : 0;
    return mesh;
  }
  catch (const Standard_Failure& failure)
  {
    Say(error, errorCapacity, std::string("Open CASCADE: ") + failure.what());
    return nullptr;
  }
  catch (const std::exception& ex)
  {
    Say(error, errorCapacity, ex.what());
    return nullptr;
  }
}

// Copies a read mesh out: xyz (3 per vertex, mm), tri (3 per triangle), rgb (3 per triangle).
RW_API void rw_occt_mesh(void* handle, double* xyz, int* tri, unsigned char* rgb)
{
  auto* mesh = static_cast<Mesh*>(handle);
  if (mesh == nullptr) return;
  if (xyz) memcpy(xyz, mesh->xyz.data(), mesh->xyz.size() * sizeof(double));
  if (tri) memcpy(tri, mesh->tri.data(), mesh->tri.size() * sizeof(int));
  if (rgb) memcpy(rgb, mesh->rgb.data(), mesh->rgb.size());
}

RW_API void rw_occt_free(void* handle) { delete static_cast<Mesh*>(handle); }

// Writes a triangle mesh (mm) as STEP AP214: the triangles sewn into a shell, closed into a solid when they close.
// Returns 0, or -1 with the reason in error.
RW_API int rw_occt_write_step(const wchar_t* path, const double* xyz, int vertexCount, const int* tri, int triangleCount,
                              char* error, int errorCapacity)
{
  Quiet();
  try
  {
    if (vertexCount <= 0 || triangleCount <= 0)
    {
      Say(error, errorCapacity, "empty mesh");
      return -1;
    }

    double minX = 1e300, maxX = -1e300, minY = 1e300, maxY = -1e300, minZ = 1e300, maxZ = -1e300;
    for (int i = 0; i < vertexCount; ++i)
    {
      minX = std::fmin(minX, xyz[3 * i]);
      maxX = std::fmax(maxX, xyz[3 * i]);
      minY = std::fmin(minY, xyz[3 * i + 1]);
      maxY = std::fmax(maxY, xyz[3 * i + 1]);
      minZ = std::fmin(minZ, xyz[3 * i + 2]);
      maxZ = std::fmax(maxZ, xyz[3 * i + 2]);
    }

    double size = std::sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY) + (maxZ - minZ) * (maxZ - minZ));
    double tolerance = std::fmax(1e-6, size * 1e-6);
    BRepBuilderAPI_Sewing sewing(tolerance * 10);
    int faces = 0;
    for (int t = 0; t < triangleCount; ++t)
    {
      int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
      if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount) continue;
      gp_Pnt pa(xyz[3 * a], xyz[3 * a + 1], xyz[3 * a + 2]);
      gp_Pnt pb(xyz[3 * b], xyz[3 * b + 1], xyz[3 * b + 2]);
      gp_Pnt pc(xyz[3 * c], xyz[3 * c + 1], xyz[3 * c + 2]);
      if (pa.Distance(pb) <= tolerance || pb.Distance(pc) <= tolerance || pc.Distance(pa) <= tolerance) continue;   // degenerate
      BRepBuilderAPI_MakePolygon polygon(pa, pb, pc, true);
      if (!polygon.IsDone()) continue;
      BRepBuilderAPI_MakeFace face(polygon.Wire(), true);
      if (!face.IsDone()) continue;
      sewing.Add(face.Face());
      faces++;
    }

    if (faces == 0)
    {
      Say(error, errorCapacity, "no valid triangles");
      return -1;
    }

    sewing.Perform();
    TopoDS_Shape sewn = sewing.SewedShape();
    TopoDS_Shape result = sewn;
    // A closed shell becomes a solid (what CAD programs expect to open as a body).
    if (!sewn.IsNull() && sewn.ShapeType() == TopAbs_SHELL && BRep_Tool::IsClosed(sewn))
    {
      BRepBuilderAPI_MakeSolid solid(TopoDS::Shell(sewn));
      if (solid.IsDone()) result = solid.Solid();
    }

    STEPControl_Writer writer;
    Interface_Static::SetCVal("write.step.unit", "MM");
    Interface_Static::SetCVal("write.step.schema", "AP214IS");
    if (writer.Transfer(result, STEPControl_AsIs) != IFSelect_RetDone)
    {
      Say(error, errorCapacity, "the shape could not be transferred to STEP");
      return -1;
    }

    if (writer.Write(Utf8(path).c_str()) != IFSelect_RetDone)
    {
      Say(error, errorCapacity, "the STEP file could not be written");
      return -1;
    }

    return 0;
  }
  catch (const Standard_Failure& failure)
  {
    Say(error, errorCapacity, std::string("Open CASCADE: ") + failure.what());
    return -1;
  }
  catch (const std::exception& ex)
  {
    Say(error, errorCapacity, ex.what());
    return -1;
  }
}
