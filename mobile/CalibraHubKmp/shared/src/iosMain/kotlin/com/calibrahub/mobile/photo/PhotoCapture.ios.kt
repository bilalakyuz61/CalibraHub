package com.calibrahub.mobile.photo

import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import kotlinx.cinterop.ExperimentalForeignApi
import kotlinx.cinterop.addressOf
import kotlinx.cinterop.usePinned
import kotlinx.coroutines.suspendCancellableCoroutine
import platform.Foundation.NSData
import platform.UIKit.UIApplication
import platform.UIKit.UIImage
import platform.UIKit.UIImageJPEGRepresentation
import platform.UIKit.UIImagePickerController
import platform.UIKit.UIImagePickerControllerDelegateProtocol
import platform.UIKit.UIImagePickerControllerOriginalImage
import platform.UIKit.UIImagePickerControllerSourceType
import platform.UIKit.UINavigationControllerDelegateProtocol
import platform.UIKit.UIViewController
import platform.darwin.NSObject
import kotlin.coroutines.resume

/**
 * iOS actual — `UIImagePickerController` ile kamera acar.
 *
 * Neden UIImagePickerController (PHPicker degil): PHPicker yalnizca KUTUPHANEDEN secim yapar,
 * kamera acamaz. Kanit fotografi "o anda cek" senaryosudur, dolayisiyla dogru API budur.
 * (Apple UIImagePickerController'i galeri icin deprecate etti; KAMERA kaynagi icin gecerli.)
 *
 * Kamera yoksa (simulator) veya kullanici iptal ederse `null` doner — bunlar HATA DEGILDIR.
 * `NSCameraUsageDescription` iosApp/project.yml icinde zaten tanimli (barkod tarama icin).
 */
@OptIn(ExperimentalForeignApi::class)
actual class PhotoCapture {

    actual suspend fun capture(): CapturedPhoto? = suspendCancellableCoroutine { cont ->
        val root = UIApplication.sharedApplication.keyWindow?.rootViewController
        if (root == null || !UIImagePickerController.isSourceTypeAvailable(
                UIImagePickerControllerSourceType.UIImagePickerControllerSourceTypeCamera)
        ) {
            cont.resume(null)
            return@suspendCancellableCoroutine
        }

        val picker = UIImagePickerController()
        picker.sourceType = UIImagePickerControllerSourceType.UIImagePickerControllerSourceTypeCamera

        // Delegate GUCLU bir referansla tutulmali: picker.delegate zayif (weak) tutar,
        // yerel degisken kapsamdan cikinca delegate serbest kalir ve geri cagri HIC gelmez.
        // Bu yuzden nesne closure tarafindan yakalanip picker'a atanana kadar canli tutulur.
        val delegate = object : NSObject(), UIImagePickerControllerDelegateProtocol,
            UINavigationControllerDelegateProtocol {

            override fun imagePickerController(
                picker: UIImagePickerController,
                didFinishPickingMediaWithInfo: Map<Any?, *>,
            ) {
                val image = didFinishPickingMediaWithInfo[UIImagePickerControllerOriginalImage] as? UIImage
                picker.dismissViewControllerAnimated(true, null)
                cont.resume(image?.let { toCapturedPhoto(it) })
            }

            override fun imagePickerControllerDidCancel(picker: UIImagePickerController) {
                picker.dismissViewControllerAnimated(true, null)
                cont.resume(null)
            }
        }

        picker.delegate = delegate
        root.presentViewController(picker, animated = true, completion = null)
    }
}

/** JPEG'e sikistirip byte dizisine cevirir. 0.85: Android tarafiyla AYNI kalite noktasi. */
@OptIn(ExperimentalForeignApi::class)
private fun toCapturedPhoto(image: UIImage): CapturedPhoto? {
    val data: NSData = UIImageJPEGRepresentation(image, 0.85) ?: return null
    val length = data.length.toInt()
    if (length <= 0) return null

    val bytes = ByteArray(length)
    bytes.usePinned { pinned ->
        platform.posix.memcpy(pinned.addressOf(0), data.bytes, data.length)
    }
    return CapturedPhoto(bytes = bytes, fileName = "foto.jpg")
}

@Composable
actual fun rememberPhotoCapture(): PhotoCapture = remember { PhotoCapture() }
