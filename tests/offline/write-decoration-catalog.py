import json
import os
import sys
from pathlib import Path

source = Path(sys.argv[1]).resolve()
target = Path(sys.argv[2]).resolve()
rows = json.loads(source.read_text(encoding="utf-8-sig"))
notes = {
    "TrophyForestTroll": "Условный fallback геометрии/материала; разворот лица",
    "TrophyFrostTroll": "Разворот лица",
    "TrophyBjorn": "Подъём головы и разворот лица",
    "TrophyBjornUndead": "Разворот лица; проверить стык у волос крупным планом",
    "TrophyKvastur": "Возвращены authored localBounds; вертикальное положение",
}
pitch = {"TrophySeeker", "TrophySeekerBrute", "TrophyWrithan", "TrophyWolf", "TrophyWraith", "TrophyFenring", "TrophyMole", "TrophyGoblinShaman", "TrophyGoblinKing", "TrophyAbomination", "TrophyBonemass"}
yaw = {"TrophyDraugr", "TrophyDraugrElite", "TrophyDraugrFem", "TrophyGoblin", "TrophyGoblinBrute", "TrophyJotunWarrior", "TrophyJotunWitch"}
lines = ["# Каталог украшений — игровой просмотр", "", f"Источник: {len(rows)} записей из текущего ObjectDB изолированного полного профиля. Таблица описывает фактический рендер, не баланс и не доступность предмета на стадии прохождения.", "", "На листах предметы идут справа налево: верхний ряд, средний, нижний. Цепи показаны увеличенными вместе с моделью. Кадры не доказывают отсутствие пересечений во всех позах и на всей экипировке.", "", "| № | Предмет | Prefab | Геометрия / материалы / иконка | Кадр | Исправление или замечание |", "| --- | --- | --- | --- | --- | --- |"]
for row in rows:
    page = source.parent / f"page-{row['page']:02d}.png"
    link = Path(os.path.relpath(page, target.parent)).as_posix()
    status = " / ".join("да" if row.get(key) else "НЕТ" for key in ("geometryVisible", "materialsValid", "icon"))
    name = row["prefab"]
    note = notes.get(name, "Вертикальное положение" if name in pitch else "Разворот лица" if name in yaw else "Кадр просмотрен; общая нормализация размера")
    lines.append(f"| {row['index'] + 1} | {row['name'].replace('|', '/')} | {name} | {status} | [лист {row['page'] + 1}]({link}) | {note} |")
target.write_text("\n".join(lines) + "\n", encoding="utf-8")
print(f"Wrote {len(rows)} catalogue rows: {target}")
