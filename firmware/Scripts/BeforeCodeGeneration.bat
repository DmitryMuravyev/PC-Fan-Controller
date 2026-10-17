@echo off

REM set SCRIPT_DIR=%CD%
REM set SCRIPT_DIR=%0%
REM set SCRIPT_DIR=%~dp0%
REM set SCRIPT_DIR=%~f0%

for %%A in ("%~dp0.") do set PROJECT_DIR=%%~dpA

REM Rename main.cpp to main.c (CubeMX only updates main.c)
set CORE_SRC_PATH=%PROJECT_DIR%Core\Src\

if exist "%CORE_SRC_PATH%main.cpp" (
	if exist "%CORE_SRC_PATH%main.c" (
		echo ERROR: Both main.c and main.cpp exist in Core\Src. Keep only the actual one and restart the code generation.
		exit /b 1
	)
	ren "%CORE_SRC_PATH%main.cpp" main.c
)
