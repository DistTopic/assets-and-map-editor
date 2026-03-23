import xml.etree.ElementTree as ET
import os
os.chdir('/Users/brewertonsantos/dev/pokeorigins-tibia/assets-and-map-editor')

# Check borders
tree = ET.parse('src/App/data/brushes/borders.xml')
for border in tree.findall('.//border'):
    if border.get('id') is None:
        print(f'BORDER MISSING ID: {ET.tostring(border, encoding="unicode")[:200]}')

# Check grounds 
tree2 = ET.parse('src/App/data/brushes/grounds.xml')
for brush in tree2.findall('.//brush'):
    if brush.get('type') != 'ground':
        continue
    for item in brush.findall('item'):
        if item.get('id') is None:
            print(f'ITEM MISSING ID in brush={brush.get("name")}')
    for border in brush.findall('border'):
        if border.get('id') is None:
            print(f'BORDER MISSING ID in brush={brush.get("name")}')
    for friend in brush.findall('friend'):
        if friend.get('name') is None:
            print(f'FRIEND MISSING NAME in brush={brush.get("name")}')
    opt = brush.find('optional')
    if opt is not None and opt.get('id') is None:
        print(f'OPTIONAL MISSING ID in brush={brush.get("name")}')

print('Done checking')
