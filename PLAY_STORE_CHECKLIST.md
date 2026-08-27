# ФотоАрхив — чек-лист Google Play

## Уже подготовлено в проекте

- `targetSdk = 36`, `compileSdk = 36`.
- Production label: `ФотоАрхив`.
- OAuth code flow переведён на PKCE; client secret не включается в APK.
- Добавлен in-app disclosure перед запросом доступа к фото и видео.
- Добавлен шаблон `privacy-policy.html`.
- Добавлена release-конфигурация через переменные окружения.

## Перед загрузкой в Play Console

1. Вписать реальный email владельца в `privacy-policy.html`.
2. Разместить файл на публичном HTTPS-адресе.
3. Создать upload keystore и задать:
   - `RELEASE_STORE_FILE`
   - `RELEASE_STORE_PASSWORD`
   - `RELEASE_KEY_ALIAS`
   - `RELEASE_KEY_PASSWORD`
4. Собрать `./gradlew bundleRelease`.
5. В Play Console заполнить Privacy Policy и Data safety.
6. Для `READ_MEDIA_IMAGES` и `READ_MEDIA_VIDEO` подать Photo/Video Permissions declaration, обосновав резервное копирование всей медиатеки как core functionality.
7. Добавить иконку, скриншоты, описание, возрастной рейтинг и email поддержки.
8. Проверить закрытое тестирование, если аккаунт разработчика подпадает под это требование.

## Описание Data safety для проверки

- Фото и видео: собираются/передаются по действию пользователя для резервного копирования.
- OAuth-токен: хранится локально для авторизации в Яндекс Диске.
- Передача: Яндекс OAuth и API Яндекс Диска, HTTPS.
- Реклама и продажа данных: нет.
- Удаление локальных данных: выход из аккаунта удаляет локальный токен.
