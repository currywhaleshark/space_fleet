"""Assemble unaltered Blender review renders with labels (not a concept image)."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
here=Path(__file__).resolve().parent
sheet=Image.new('RGB',(1600,1220),(21,29,37));draw=ImageDraw.Draw(sheet)
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',26)
small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',21)
draw.text((28,16),'T3 순양함 4종 · Blender 1차 기본형',font=font,fill=(225,233,239))
items=[('earth_gun_cruiser','다뉴브 · 장갑 선수와 포격 갑판'),
 ('earth_air_defense_cruiser','시러스 · 전후 방공 갑판'),
 ('mars_gun_cruiser','칸도르 · 가는 선체와 장포신'),
 ('mars_missile_cruiser','크리세 · 양측 미사일 카세트')]
for i,(key,label) in enumerate(items):
 x=(i%2)*800;y=66+(i//2)*575
 img=Image.open(here/(key+'_hero.png')).convert('RGB');img.thumbnail((800,520))
 sheet.paste(img,(x+(800-img.width)//2,y))
 draw.text((x+28,y+521),label,font=small,fill=(212,223,232))
sheet.save(here/'cruisers-overview.png')
