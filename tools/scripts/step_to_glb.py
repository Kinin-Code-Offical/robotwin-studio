import argparse
from pathlib import Path


def _mesh_document_shapes(doc) -> None:
    from OCP.BRepMesh import BRepMesh_IncrementalMesh
    from OCP.XCAFDoc import XCAFDoc_DocumentTool
    from OCP.TDF import TDF_LabelSequence

    shape_tool = XCAFDoc_DocumentTool.ShapeTool_s(doc.Main())
    labels = TDF_LabelSequence()
    shape_tool.GetFreeShapes(labels)
    for idx in range(1, labels.Length() + 1):
        label = labels.Value(idx)
        shape = shape_tool.GetShape_s(label)
        if shape.IsNull():
            continue
        BRepMesh_IncrementalMesh(shape, 0.25)


def convert_step_to_glb(step_path: Path, glb_path: Path) -> None:
    from OCP.TCollection import TCollection_ExtendedString, TCollection_AsciiString
    from OCP.TDocStd import TDocStd_Document
    from OCP.XCAFApp import XCAFApp_Application
    from OCP.STEPCAFControl import STEPCAFControl_Reader
    from OCP.IFSelect import IFSelect_RetDone
    from OCP.RWGltf import RWGltf_CafWriter
    from OCP.TColStd import TColStd_IndexedDataMapOfStringString
    from OCP.Message import Message_ProgressRange

    app = XCAFApp_Application.GetApplication_s()
    doc = TDocStd_Document(TCollection_ExtendedString("RTWIN"))
    app.NewDocument(TCollection_ExtendedString("MDTV-XCAF"), doc)

    reader = STEPCAFControl_Reader()
    reader.SetColorMode(True)
    reader.SetNameMode(True)
    reader.SetLayerMode(True)
    reader.SetMatMode(True)

    status = reader.ReadFile(str(step_path))
    if status != IFSelect_RetDone:
        raise RuntimeError(f"STEP read failed: status={int(status)} path={step_path}")

    if not reader.Transfer(doc):
        raise RuntimeError(f"STEP transfer failed: {step_path}")

    _mesh_document_shapes(doc)

    writer = RWGltf_CafWriter(TCollection_AsciiString(str(glb_path)), True)
    writer.SetToEmbedTexturesInGlb(True)
    file_info = TColStd_IndexedDataMapOfStringString()
    progress = Message_ProgressRange()
    if not writer.Perform(doc, file_info, progress):
        raise RuntimeError(f"GLB write failed: {glb_path}")

    if not glb_path.exists() or glb_path.stat().st_size == 0:
        raise RuntimeError(f"GLB output missing/empty: {glb_path}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Convert STEP/STP to GLB using OpenCascade (cadquery-ocp).")
    parser.add_argument("input", type=Path, help="Input .step/.stp path")
    parser.add_argument("output", type=Path, help="Output .glb path")
    args = parser.parse_args()

    step_path: Path = args.input
    glb_path: Path = args.output

    if not step_path.exists():
        raise SystemExit(f"Input not found: {step_path}")
    glb_path.parent.mkdir(parents=True, exist_ok=True)
    convert_step_to_glb(step_path, glb_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
