# Generates placeholder COLLADA models and navigation icons (own work, procedural).
# Usage: powershell -File tools/assets/Generate-Assets.ps1
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$modelDir = Join-Path $root 'assets/models'
$iconDir = Join-Path $root 'assets/icons'
New-Item -ItemType Directory -Force $modelDir, $iconDir | Out-Null

function F([double]$v) { $v.ToString('0.####', $inv) }

# A mesh is a hashtable: material -> list of triangles; triangle = 3 points (x,y,z arrays).
function Add-Tri($mesh, $mat, $a, $b, $c) {
    if (-not $mesh.ContainsKey($mat)) { $mesh[$mat] = New-Object System.Collections.ArrayList }
    [void]$mesh[$mat].Add(@($a, $b, $c))
}
function Add-Quad($mesh, $mat, $a, $b, $c, $d) {
    Add-Tri $mesh $mat $a $b $c
    Add-Tri $mesh $mat $a $c $d
}
function Add-Frustum($mesh, $mat, $r0, $r1, $z0, $z1, $seg, $bottomCap) {
    for ($i = 0; $i -lt $seg; $i++) {
        $t0 = 2 * [math]::PI * $i / $seg; $t1 = 2 * [math]::PI * ($i + 1) / $seg
        $p0 = @(($r0 * [math]::Cos($t0)), ($r0 * [math]::Sin($t0)), $z0)
        $p1 = @(($r0 * [math]::Cos($t1)), ($r0 * [math]::Sin($t1)), $z0)
        $q0 = @(($r1 * [math]::Cos($t0)), ($r1 * [math]::Sin($t0)), $z1)
        $q1 = @(($r1 * [math]::Cos($t1)), ($r1 * [math]::Sin($t1)), $z1)
        if ($r1 -eq 0) { Add-Tri $mesh $mat $p0 $p1 $q0 } else { Add-Quad $mesh $mat $p0 $p1 $q1 $q0 }
        if ($bottomCap) { Add-Tri $mesh $mat @(0, 0, $z0) $p1 $p0 }
    }
}
function Add-Box($mesh, $mat, $x0, $x1, $y0, $y1, $z0, $z1) {
    $p = @(@($x0,$y0,$z0),@($x1,$y0,$z0),@($x1,$y1,$z0),@($x0,$y1,$z0),@($x0,$y0,$z1),@($x1,$y0,$z1),@($x1,$y1,$z1),@($x0,$y1,$z1))
    Add-Quad $mesh $mat $p[0] $p[3] $p[2] $p[1]
    Add-Quad $mesh $mat $p[4] $p[5] $p[6] $p[7]
    Add-Quad $mesh $mat $p[0] $p[1] $p[5] $p[4]
    Add-Quad $mesh $mat $p[1] $p[2] $p[6] $p[5]
    Add-Quad $mesh $mat $p[2] $p[3] $p[7] $p[6]
    Add-Quad $mesh $mat $p[3] $p[0] $p[4] $p[7]
}

function Write-Dae($path, $name, $mesh, $colors) {
    $mats = @($mesh.Keys | Sort-Object)
    $q = '"'
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$sb.AppendLine('<COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">')
    [void]$sb.AppendLine('  <asset>')
    [void]$sb.AppendLine('    <contributor><authoring_tool>tools/assets/Generate-Assets.ps1 (procedural, own work)</authoring_tool></contributor>')
    [void]$sb.AppendLine('    <created>2026-01-01T00:00:00Z</created>')
    [void]$sb.AppendLine('    <modified>2026-01-01T00:00:00Z</modified>')
    [void]$sb.AppendLine('    <unit name="meter" meter="1"/>')
    [void]$sb.AppendLine('    <up_axis>Z_UP</up_axis>')
    [void]$sb.AppendLine('  </asset>')
    [void]$sb.AppendLine('  <library_effects>')
    foreach ($m in $mats) {
        $c = $colors[$m]
        [void]$sb.AppendLine("    <effect id=${q}$m-fx${q}><profile_COMMON><technique sid=${q}common${q}><phong><diffuse><color>$c</color></diffuse></phong></technique></profile_COMMON></effect>")
    }
    [void]$sb.AppendLine('  </library_effects>')
    [void]$sb.AppendLine('  <library_materials>')
    foreach ($m in $mats) { [void]$sb.AppendLine("    <material id=${q}$m${q} name=${q}$m${q}><instance_effect url=${q}#$m-fx${q}/></material>") }
    [void]$sb.AppendLine('  </library_materials>')
    [void]$sb.AppendLine('  <library_geometries>')
    [void]$sb.AppendLine("    <geometry id=${q}$name-mesh${q} name=${q}$name${q}><mesh>")
    $pos = New-Object System.Text.StringBuilder; $nor = New-Object System.Text.StringBuilder
    $tris = @{}; $n = 0
    foreach ($m in $mats) {
        $idx = New-Object System.Text.StringBuilder
        foreach ($t in $mesh[$m]) {
            $a = $t[0]; $b = $t[1]; $c = $t[2]
            $u = @(($b[0]-$a[0]), ($b[1]-$a[1]), ($b[2]-$a[2])); $w = @(($c[0]-$a[0]), ($c[1]-$a[1]), ($c[2]-$a[2]))
            $nx = $u[1]*$w[2]-$u[2]*$w[1]; $ny = $u[2]*$w[0]-$u[0]*$w[2]; $nz = $u[0]*$w[1]-$u[1]*$w[0]
            $l = [math]::Sqrt($nx*$nx+$ny*$ny+$nz*$nz); if ($l -eq 0) { $l = 1 }
            foreach ($p in @($a, $b, $c)) {
                [void]$pos.Append("$(F $p[0]) $(F $p[1]) $(F $p[2]) ")
                [void]$nor.Append("$(F ($nx/$l)) $(F ($ny/$l)) $(F ($nz/$l)) ")
                [void]$idx.Append("$n "); $n++
            }
        }
        $tris[$m] = $idx.ToString().Trim()
    }
    $acc = "<technique_common><accessor source=${q}#@@-arr${q} count=${q}$n${q} stride=${q}3${q}><param name=${q}X${q} type=${q}float${q}/><param name=${q}Y${q} type=${q}float${q}/><param name=${q}Z${q} type=${q}float${q}/></accessor></technique_common>"
    [void]$sb.AppendLine("      <source id=${q}$name-pos${q}><float_array id=${q}$name-pos-arr${q} count=${q}$($n*3)${q}>$($pos.ToString().Trim())</float_array>$($acc.Replace('@@', "$name-pos"))</source>")
    [void]$sb.AppendLine("      <source id=${q}$name-nor${q}><float_array id=${q}$name-nor-arr${q} count=${q}$($n*3)${q}>$($nor.ToString().Trim())</float_array>$($acc.Replace('@@', "$name-nor"))</source>")
    [void]$sb.AppendLine("      <vertices id=${q}$name-vtx${q}><input semantic=${q}POSITION${q} source=${q}#$name-pos${q}/></vertices>")
    foreach ($m in $mats) {
        $cnt = ($tris[$m] -split ' ').Count / 3
        [void]$sb.AppendLine("      <triangles material=${q}$m-sym${q} count=${q}$cnt${q}><input semantic=${q}VERTEX${q} source=${q}#$name-vtx${q} offset=${q}0${q}/><input semantic=${q}NORMAL${q} source=${q}#$name-nor${q} offset=${q}0${q}/><p>$($tris[$m])</p></triangles>")
    }
    [void]$sb.AppendLine('    </mesh></geometry>')
    [void]$sb.AppendLine('  </library_geometries>')
    [void]$sb.AppendLine("  <library_visual_scenes><visual_scene id=${q}scene${q}><node id=${q}$name${q} name=${q}$name${q}><instance_geometry url=${q}#$name-mesh${q}><bind_material><technique_common>")
    foreach ($m in $mats) { [void]$sb.AppendLine("      <instance_material symbol=${q}$m-sym${q} target=${q}#$m${q}/>") }
    [void]$sb.AppendLine('    </technique_common></bind_material></instance_geometry></node></visual_scene></library_visual_scenes>')
    [void]$sb.AppendLine('  <scene><instance_visual_scene url="#scene"/></scene>')
    [void]$sb.AppendLine('</COLLADA>')
    [IO.File]::WriteAllText($path, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
}

# --- Starship placeholder: r=4.5 m, body z 0..40, nose cone z 40..50, 2 aft fins + 2 forward flaps ---
$s = @{}
Add-Frustum $s 'hull' 4.5 4.5 0 40 24 $true
Add-Frustum $s 'nose' 4.5 0 40 50 24 $false
Add-Box $s 'fin' -1.0 1.0 4.5 9.5 0 8
Add-Box $s 'fin' -1.0 1.0 -9.5 -4.5 0 8
Add-Box $s 'fin' 4.5 8.5 -0.75 0.75 33 40
Add-Box $s 'fin' -8.5 -4.5 -0.75 0.75 33 40
Write-Dae (Join-Path $modelDir 'starship.dae') 'starship' $s @{ hull = '0.85 0.85 0.88 1'; nose = '0.55 0.55 0.6 1'; fin = '0.25 0.25 0.28 1' }

# --- Super Heavy placeholder: r=4.5 m, z 0..70, short cap to 72, four small grid-fin boxes near top ---
$b = @{}
Add-Frustum $b 'hull' 4.5 4.5 0 70 24 $true
Add-Frustum $b 'cap' 4.5 3.2 70 72 24 $false
Add-Box $b 'fin' 4.5 7.5 -0.5 0.5 62 66
Add-Box $b 'fin' -7.5 -4.5 -0.5 0.5 62 66
Add-Box $b 'fin' -0.5 0.5 4.5 7.5 62 66
Add-Box $b 'fin' -0.5 0.5 -7.5 -4.5 62 66
Write-Dae (Join-Path $modelDir 'superheavy.dae') 'superheavy' $b @{ hull = '0.7 0.72 0.78 1'; cap = '0.4 0.42 0.48 1'; fin = '0.2 0.2 0.22 1' }

# --- Icons: 64x64, transparent background, drawn at 256 px then downscaled ---
Add-Type -AssemblyName System.Drawing
function New-Icon($path, $draw) {
    $big = New-Object Drawing.Bitmap 256, 256
    $g = [Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::Transparent)
    & $draw $g
    $g.Dispose()
    $bmp = New-Object Drawing.Bitmap 64, 64
    $g2 = [Drawing.Graphics]::FromImage($bmp)
    $g2.InterpolationMode = 'HighQualityBicubic'; $g2.DrawImage($big, 0, 0, 64, 64); $g2.Dispose()
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose(); $big.Dispose()
}
$dark = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 20, 24, 40)), 14
$dark.LineJoin = 'Round'
New-Icon (Join-Path $iconDir 'starship.png') {
    param($g)
    $pts = [Drawing.PointF[]]@((New-Object Drawing.PointF 128, 20), (New-Object Drawing.PointF 220, 230), (New-Object Drawing.PointF 128, 180), (New-Object Drawing.PointF 36, 230))
    $g.FillPolygon([Drawing.Brushes]::White, $pts); $g.DrawPolygon($dark, $pts)
}
New-Icon (Join-Path $iconDir 'booster.png') {
    param($g)
    $orange = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 255, 160, 0))
    $g.FillRectangle($orange, 84, 24, 88, 208); $g.DrawRectangle($dark, 84, 24, 88, 208)
    $g.FillEllipse($orange, 84, 4, 88, 40); $g.DrawEllipse($dark, 84, 4, 88, 40)
}
Write-Host "Assets generated in $modelDir and $iconDir"
