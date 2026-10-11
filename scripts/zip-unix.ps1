function Set-ZipUnixOrigin([string]$Path, [switch]$VerifyOnly) {
    $access = if ($VerifyOnly) { [IO.FileAccess]::Read } else { [IO.FileAccess]::ReadWrite }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, $access)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        # Unix modes are ignored by unzip unless the central-directory creator is Unix.
        $tailLength = [int][Math]::Min(65557, $stream.Length)
        $stream.Position = $stream.Length - $tailLength
        $tail = $reader.ReadBytes($tailLength)
        $end = -1
        for ($i = $tail.Length - 22; $i -ge 0; $i--) {
            if ([BitConverter]::ToUInt32($tail, $i) -eq 0x06054b50 -and
                $i + 22 + [BitConverter]::ToUInt16($tail, $i + 20) -eq $tail.Length) { $end = $i; break }
        }
        if ($end -lt 0) { throw 'ZIP end record missing.' }
        $count = [BitConverter]::ToUInt16($tail, $end + 10)
        $offset = [BitConverter]::ToUInt32($tail, $end + 16)
        if ($count -eq 65535 -or $offset -eq [uint32]::MaxValue -or
            [BitConverter]::ToUInt32($tail, $end + 4) -ne 0) { throw 'ZIP64 and multipart archives are not supported.' }
        $stream.Position = $offset
        for ($i = 0; $i -lt $count; $i++) {
            $start = $stream.Position
            $header = $reader.ReadBytes(46)
            if ($header.Length -ne 46 -or [BitConverter]::ToUInt32($header, 0) -ne 0x02014b50) { throw 'Invalid ZIP central directory.' }
            if ($VerifyOnly) {
                if ($header[5] -ne 3) { throw 'ZIP entry lacks Unix creator metadata.' }
            } else {
                $stream.Position = $start + 5
                $stream.WriteByte(3)
            }
            $stream.Position = $start + 46 + [BitConverter]::ToUInt16($header, 28) +
                [BitConverter]::ToUInt16($header, 30) + [BitConverter]::ToUInt16($header, 32)
            if ($stream.Position -gt $stream.Length) { throw 'ZIP entry extends beyond the archive.' }
        }
    } finally { $reader.Dispose(); $stream.Dispose() }
}
