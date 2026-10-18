# FFmpeg rebuild materials / Материалы сборки FFmpeg

This source checkout includes only the original build recipes, response-file patch, provenance manifest, notice and LGPL text. No FFmpeg executable or complete upstream source archive is bundled here. The mod invokes FFmpeg/ffprobe as separate processes.

For the matching 7.1.5 source archive, use the upstream URL and SHA256 in `manifest.json`. The archive must match that hash. For a release distribution, arrange:

```text
distribution/
  bin/ffmpeg.exe
  bin/ffprobe.exe
  manifest.json
  LICENSE
  README.txt
  Sources/
    ffmpeg-7.1.5.tar.xz
    ffmpeg-7.1.5.tar.xz.asc
    build-local.ps1
    build-local.sh
    library-response-file.patch
```

Read `README.txt` for exact tool versions, prerequisites and patch/build steps. Its `Tools/FFmpeg` paths describe the install ZIP. Adapt the compiler and tool locations in the build scripts to your installation. Package rebuilt executables with corresponding-source materials and update provenance hashes if they differ. `package.ps1` checks source and executable hashes. A compiler change may change executable hashes. These tools decode audio and are not general-purpose video encoders.

Здесь только исходные инструкции сборки, патч response-file, манифест происхождения, уведомление и LGPL. Программ FFmpeg и полного исходного архива здесь нет. Они вызываются модом отдельными процессами. Адрес исходников 7.1.5 и контрольная сумма находятся в `manifest.json`; архив должен совпадать по SHA256. Структура полного дистрибутива для упаковки показана выше.

Версии инструментов и этапы подготовки описаны в `README.txt`. Его пути `Tools/FFmpeg` относятся к установочному ZIP. Адаптируйте пути компилятора и инструментов в скриптах под свою установку. При пересборке приложите соответствующие исходные материалы и обновите хеши, если результат отличается. Упаковщик сверяет программы и архив. Другой компилятор может дать другой хеш. Это декодеры звука, не универсальный видеокодировщик.
