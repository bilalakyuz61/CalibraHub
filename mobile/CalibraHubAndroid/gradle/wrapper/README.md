# Gradle Wrapper

`gradle-wrapper.jar` binary dosyası bu commit'te bulunmuyor. Android Studio projeyi ilk açtığında otomatik üretir ve indirir. Manuel oluşturmak istersen:

```powershell
# CalibraHubAndroid/ klasöründe:
gradle wrapper --gradle-version 8.5
```

Bu komut `gradle-wrapper.jar` + `gradlew` + `gradlew.bat` dosyalarını oluşturur.

`gradle-wrapper.properties` zaten manuel olarak yazıldı (Gradle 8.5 versiyonu sabitlenir).
