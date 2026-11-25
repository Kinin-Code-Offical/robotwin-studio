@echo off
rem [occt] Builds RobotWin's Open CASCADE bridge into third_party\occt\win-x64 (what the app ships in its occt folder).
rem
rem   tools\scripts\build_occt.bat [path\to\OCCT-8_0_2 source]
rem
rem Needs Visual Studio 2022 Build Tools (C++, CMake, Ninja). The source is Open CASCADE Technology V8_0_2 from
rem https://github.com/Open-Cascade-SAS/OCCT (tag V8_0_2), unpacked at a short path (default .o\s: OCCT's file names
rem pass Windows' 260-character limit from deep folders). Only the toolkits the bridge needs are built, with no
rem third-party libraries (no FreeType, FreeImage, FFmpeg, OpenVR, TBB or jemalloc): the bridge reads and writes
rem files, it draws nothing.
setlocal
set ROOT=%~dp0..\..
for %%i in ("%ROOT%") do set ROOT=%%~fi
set S=%~1
if "%S%"=="" set S=%ROOT%\.o\s
set B=%ROOT%\.o\b
set OUT=%ROOT%\third_party\occt\win-x64
set VS=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools
if not exist "%VS%\VC\Auxiliary\Build\vcvars64.bat" set VS=C:\Program Files\Microsoft Visual Studio\2022\Community
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
set CMAKE=%VS%\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe
set NINJA=%VS%\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe
if not exist "%S%\CMakeLists.txt" (echo OCCT source not found at %S% & exit /b 1)
if not exist "%B%" mkdir "%B%"
pushd "%B%"
"%CMAKE%" -G Ninja "%S%" -DCMAKE_BUILD_TYPE=Release -DBUILD_LIBRARY_TYPE=Shared -DBUILD_MODULE_Draw=OFF ^
  -DUSE_FREETYPE=OFF -DUSE_TK=OFF -DUSE_TCL=OFF -DUSE_OPENGL=OFF -DUSE_GLES2=OFF -DUSE_D3D=OFF -DUSE_TBB=OFF ^
  -DUSE_VTK=OFF -DUSE_FREEIMAGE=OFF -DUSE_FFMPEG=OFF -DUSE_OPENVR=OFF -DUSE_RAPIDJSON=OFF -DUSE_DRACO=OFF ^
  -DUSE_MMGR_TYPE=NATIVE -DBUILD_DOC_Overview=OFF -DBUILD_USE_PCH=OFF || exit /b 1
"%NINJA%" TKDESTEP TKDEIGES TKMesh TKRWMesh || exit /b 1
popd
if not exist "%B%\shim" mkdir "%B%\shim"
pushd "%B%\shim"
cl /nologo /O2 /EHsc /MD /std:c++17 /DNDEBUG /I"%B%\inc" "%ROOT%\CoreSim\native\occt\rw_occt.cpp" /LD /Fe:RobotWin.Occt.dll ^
  /link /LIBPATH:"%B%\win64\vc14\lib" TKernel.lib TKMath.lib TKBRep.lib TKG2d.lib TKG3d.lib TKGeomBase.lib TKTopAlgo.lib ^
  TKMesh.lib TKXSBase.lib TKDESTEP.lib TKDEIGES.lib TKXCAF.lib TKLCAF.lib TKCAF.lib TKCDF.lib TKRWMesh.lib TKDE.lib || exit /b 1
popd
if not exist "%OUT%" mkdir "%OUT%"
copy /y "%B%\shim\RobotWin.Occt.dll" "%OUT%" >nul
rem The toolkits the bridge loads (its dependency closure) and the Visual C++ runtime beside them.
for %%t in (TKernel TKMath TKG2d TKG3d TKGeomBase TKBRep TKGeomAlgo TKTopAlgo TKPrim TKBO TKBool TKShHealing TKMesh TKHLR TKService TKV3d TKCDF TKLCAF TKCAF TKVCAF TKXCAF TKXSBase TKDE TKDESTEP TKDEIGES TKRWMesh) do copy /y "%B%\win64\vc14\bin\%%t.dll" "%OUT%" >nul
for /d %%r in ("%VS%\VC\Redist\MSVC\*") do if exist "%%r\x64\Microsoft.VC143.CRT\msvcp140.dll" set CRT=%%r\x64\Microsoft.VC143.CRT
for %%f in (msvcp140.dll msvcp140_2.dll vcruntime140.dll vcruntime140_1.dll) do copy /y "%CRT%\%%f" "%OUT%" >nul
copy /y "%S%\LICENSE_LGPL_21.txt" "%ROOT%\third_party\occt\" >nul
copy /y "%S%\OCCT_LGPL_EXCEPTION.txt" "%ROOT%\third_party\occt\" >nul
echo Open CASCADE bridge in %OUT%
