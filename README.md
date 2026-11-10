# battle.net account switcher

Переключатель аккаунтов Battle.net для Windows. Выберите профиль по имени и email, чтобы открыть официальный лаунчер с нужным аккаунтом. В каждый момент используется один аккаунт.

## Быстрый старт

1. Скачайте `battle.net account switcher*.exe` из раздела [**Releases**](https://github.com/GooseRuby/battle.net-account-switcher/releases) и запустите его. Устанавливать переключатель не нужно.
2. Выберите карточку и нажмите **Открыть Battle.net**. Можно также дважды щёлкнуть по карточке.
3. Если Battle.net попросит пароль или подтверждение входа, пройдите его в самом лаунчере.

Переключатель показывает аккаунты, которые запомнил Battle.net. Если нужного аккаунта нет, войдите в него в официальном лаунчере, включите сохранение входа и нажмите **Обновить** в переключателе.

## Проверка обновлений

По умолчанию переключатель проверяет [GitHub Releases](https://github.com/GooseRuby/battle.net-account-switcher/releases) в фоне при запуске. Если вышла более новая версия, в заголовке появится кнопка со ссылкой на релиз.

В **Настройках** можно отключить автопроверку или нажать **Проверить обновления**. Чтобы обновиться, откройте релиз, скачайте новый `.exe` и замените старый файл.

## Как работает переключение

При смене аккаунта переключатель закрывает лаунчер, перемещает выбранный логин на первое место в списке `SavedAccountNames` и снова открывает Battle.net. Перед изменением создаётся резервная копия `%APPDATA%\Battle.net\Battle.net.config.account-picker.bak`. Автоматический вход зависит от того, сохранил ли Battle.net действующий сеанс. Обновления лаунчера могут изменить формат настроек, от которого зависит переключение.

Имена карточек и список скрытых профилей хранятся локально в `%LOCALAPPDATA%\BattleNetAccountPicker`. Скрытие карточки действует только в переключателе. Пароли и данные сеансов обрабатывает официальный лаунчер. Переключатель не сохраняет пароли или токены входа и не отправляет данные аккаунтов в сеть.

## Сборка

Понадобятся Windows x64 и .NET Framework 4.8. Для переключения аккаунтов должен быть установлен официальный клиент Battle.net.

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" .\Switcher.csproj /t:Rebuild
```

Готовый файл появится в `bin\battle.net account switcher.exe`.

## О проекте и товарных знаках

Это независимый проект. Он не связан с Blizzard Entertainment и не одобрен ею. Название Battle.net указывает на совместимость с лаунчером. Автор проекта не претендует на права на товарные знаки Battle.net или Blizzard Entertainment. [Сведения Blizzard о товарных знаках](https://www.blizzard.com/en-us/legal/5515ca11-1c96-42a0-b853-e7876a0d19bf/copyright-notices)

---

A Windows utility for switching Battle.net accounts. Choose a profile by name and email to open the official launcher with that account. The switcher uses one account at a time.

## Quick start

1. Download `battle.net account switcher*.exe` from [**Releases**](https://github.com/GooseRuby/battle.net-account-switcher/releases) and run it. No installation is required.
2. Select a card and click **Open Battle.net**. You can also double-click the card.
3. If Battle.net asks for your password or verification, complete sign-in in the launcher.

The switcher lists accounts that Battle.net remembers. If an account is missing, sign in to it in the official launcher, enable the option to stay signed in, and click **Refresh** in the switcher.

## Check for updates

By default, the switcher checks [GitHub Releases](https://github.com/GooseRuby/battle.net-account-switcher/releases) in the background at startup. If a newer version is available, a button in the title bar links to the release.

In **Settings**, you can turn off automatic checks or click **Check for updates**. To update, open the release page, download the new `.exe`, and replace the old file.

## How switching works

When you switch accounts, the switcher closes the launcher, moves the selected login to the top of `SavedAccountNames`, and reopens Battle.net. Before editing the settings, it creates a backup at `%APPDATA%\Battle.net\Battle.net.config.account-picker.bak`. Automatic sign-in depends on Battle.net having a valid saved session. Launcher updates may change the settings format the switcher relies on.

Card names and the list of hidden profiles are stored locally in `%LOCALAPPDATA%\BattleNetAccountPicker`. Hiding a card only affects the switcher. The official launcher handles passwords and sessions. The switcher does not store passwords or sign-in tokens or send account data over the network.

## Build

You need Windows x64 and .NET Framework 4.8. To switch accounts, you need the official Battle.net launcher installed.

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" .\Switcher.csproj /t:Rebuild
```

The executable is written to `bin\battle.net account switcher.exe`.

## About and trademarks

This is an independent project. It is not affiliated with or endorsed by Blizzard Entertainment. The Battle.net name identifies the compatible launcher. The project author claims no rights to the Battle.net or Blizzard Entertainment trademarks. [Blizzard trademark notices](https://www.blizzard.com/en-us/legal/5515ca11-1c96-42a0-b853-e7876a0d19bf/copyright-notices)

