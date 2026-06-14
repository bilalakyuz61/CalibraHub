# ProGuard / R8 release shrinker rules.
# Compose + Retrofit + Moshi için varsayılan kurallar yeterli; özel tutulması
# gereken sınıflar için aşağıya ekle.

# Retrofit interface signatures (reflection)
-keep,allowobfuscation,allowshrinking interface retrofit2.Call
-keep,allowobfuscation,allowshrinking class kotlin.coroutines.Continuation

# Moshi reflection-based JSON adapter ihtimali (kotlin-reflect kullanmıyoruz ama)
-keep class com.calibrahub.app.data.** { *; }
-keepclassmembers class com.calibrahub.app.data.** { *; }
