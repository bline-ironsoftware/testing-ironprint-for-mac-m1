# Test files for the IronPrint .NET 11 tests

- `sample.pdf`: 2-page A4 colour PDF (pages numbered 1 and 2, colour bars, TOP/BOTTOM markers)
- `sample.jpg`: colour JPG (colour bars, TOP/BOTTOM markers)

What to check on paper:
- **PDF path / bytes:** both pages print, in order (1 then 2)
- **JPG:** prints with the colour bars visible
- **Grayscale:** colour bars come out as shades of grey
- **2 copies:** two full sets (1, 2, 1, 2)
- **Landscape:** the TOP arrow points along the long edge of the paper
