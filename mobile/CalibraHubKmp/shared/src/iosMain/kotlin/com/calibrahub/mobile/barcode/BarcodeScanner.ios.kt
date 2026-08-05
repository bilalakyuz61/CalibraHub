package com.calibrahub.mobile.barcode

import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import kotlinx.cinterop.CValue
import kotlinx.cinterop.ExperimentalForeignApi
import kotlinx.cinterop.ObjCAction
import kotlinx.cinterop.ObjCObjectVar
import kotlinx.cinterop.alloc
import kotlinx.cinterop.memScoped
import kotlinx.cinterop.ptr
import kotlinx.cinterop.useContents
import kotlinx.cinterop.value
import kotlin.coroutines.resume
import kotlinx.coroutines.suspendCancellableCoroutine
import platform.AVFoundation.AVAuthorizationStatusAuthorized
import platform.AVFoundation.AVAuthorizationStatusNotDetermined
import platform.AVFoundation.AVCaptureConnection
import platform.AVFoundation.AVCaptureDevice
import platform.AVFoundation.AVCaptureDeviceInput
import platform.AVFoundation.AVCaptureMetadataOutput
import platform.AVFoundation.AVCaptureMetadataOutputObjectsDelegateProtocol
import platform.AVFoundation.AVCaptureOutput
import platform.AVFoundation.AVCaptureSession
import platform.AVFoundation.AVCaptureVideoPreviewLayer
import platform.AVFoundation.AVLayerVideoGravityResizeAspectFill
import platform.AVFoundation.AVMediaTypeVideo
import platform.AVFoundation.AVMetadataMachineReadableCodeObject
import platform.AVFoundation.AVMetadataObjectTypeCode128Code
import platform.AVFoundation.AVMetadataObjectTypeCode39Code
import platform.AVFoundation.AVMetadataObjectTypeCode93Code
import platform.AVFoundation.AVMetadataObjectTypeDataMatrixCode
import platform.AVFoundation.AVMetadataObjectTypeEAN13Code
import platform.AVFoundation.AVMetadataObjectTypeEAN8Code
import platform.AVFoundation.AVMetadataObjectTypeITF14Code
import platform.AVFoundation.AVMetadataObjectTypeQRCode
import platform.AVFoundation.AVMetadataObjectTypeUPCECode
import platform.CoreGraphics.CGRectMake
import platform.Foundation.NSError
import platform.Foundation.NSSelectorFromString
import platform.UIKit.NSTextAlignmentCenter
import platform.UIKit.UIApplication
import platform.UIKit.UIButton
import platform.UIKit.UIColor
import platform.UIKit.UIControlEventTouchUpInside
import platform.UIKit.UIControlStateNormal
import platform.UIKit.UIFont
import platform.UIKit.UILabel
import platform.UIKit.UIModalPresentationFullScreen
import platform.UIKit.UIView
import platform.UIKit.UIViewController
import platform.darwin.NSObject
import platform.darwin.dispatch_async
import platform.darwin.dispatch_get_main_queue
import platform.darwin.dispatch_queue_create

/**
 * Faz 3 — iOS actual, gercek AVFoundation kamera taramasi. Faz 2a'daki STUB'un yerini alir
 * (bkz. commonMain [BarcodeScanner] KDoc'u). Tasarim, Android tarafinin (ZXing/CaptureActivity)
 * davranissal esdegeridir: [scan] modal bir tam-ekran tarayici acar, ilk okunan barkodda veya
 * kullanici "Kapat"a basinca/iptal edince suspend noktasi devam eder.
 *
 * Akis:
 * 1. Kamera izni [AVCaptureDevice.authorizationStatusForMediaType] ile kontrol edilir.
 *    - `Authorized` → dogrudan tarayici sunulur.
 *    - `NotDetermined` → [AVCaptureDevice.requestAccessForMediaType] ile izin istenir, sonuc
 *      GARANTI olmayan bir kuyrukta gelebildigi icin ana kuyruga (`dispatch_get_main_queue`)
 *      donulup UI/oturum orada kurulur (Apple dokumantasyonu bu davranisi net belirtir).
 *    - `Denied` / `Restricted` → `null` ile suspend noktasi hemen devam eder (kullanici
 *      Ayarlar'a yonlendirilmez; bu Faz'in KAPSAMI DISINDA — cagiran taraf "tarama yok"
 *      olarak yorumlar, tipki Android'de iptal davranisi gibi).
 * 2. [BarcodeScannerViewController] anahtar pencerenin en-ust `rootViewController`'i uzerinden
 *    `UIModalPresentationFullScreen` ile modal sunulur.
 * 3. [BarcodeCaptureDelegate] ilk gecerli `stringValue`'da oturumu durdurur, VC'yi kapatir ve
 *    sonucu geri cagirir; [suspendCancellableCoroutine]'in `invokeOnCancellation`'i ile dis
 *    iptal (ornegin cagiran composable'in Job'i iptal olursa) de VC'yi temizler.
 *
 * BILINEN RISK (Windows'ta derlenemedigi icin Codemagic'te dogrulanacak): AVFoundation/UIKit
 * Kotlin/Native binding imzalarinin (ozellikle [AVCaptureMetadataOutputObjectsDelegateProtocol]
 * metodunun nullability'si, `NSTextAlignmentCenter`/`UIControlStateNormal` gibi NS_ENUM/NS_OPTIONS
 * sabitlerinin top-level mi yoksa ic-ice mi expose edildigi) tam olarak elde derlenip
 * dogrulanamadi — bkz. gorev raporundaki risk listesi.
 */
@OptIn(ExperimentalForeignApi::class)
actual class BarcodeScanner internal constructor() {

    actual suspend fun scan(): String? = suspendCancellableCoroutine { cont ->
        val presenter = topViewController()
        if (presenter == null) {
            cont.resume(null)
            return@suspendCancellableCoroutine
        }

        fun presentScanner() {
            var scannerVc: BarcodeScannerViewController? = null
            scannerVc = BarcodeScannerViewController { result ->
                if (cont.isActive) cont.resume(result)
            }
            scannerVc.modalPresentationStyle = UIModalPresentationFullScreen
            presenter.presentViewController(scannerVc, animated = true, completion = null)
            cont.invokeOnCancellation {
                dispatch_async(dispatch_get_main_queue()) {
                    scannerVc?.dismissViewControllerAnimated(true, completion = null)
                }
            }
        }

        when (AVCaptureDevice.authorizationStatusForMediaType(AVMediaTypeVideo)) {
            AVAuthorizationStatusAuthorized -> presentScanner()
            AVAuthorizationStatusNotDetermined -> {
                AVCaptureDevice.requestAccessForMediaType(AVMediaTypeVideo) { granted ->
                    dispatch_async(dispatch_get_main_queue()) {
                        if (granted) presentScanner() else cont.resume(null)
                    }
                }
            }
            // Denied / Restricted (veya bilinmeyen bir gelecek deger).
            else -> cont.resume(null)
        }
    }
}

@Composable
actual fun rememberBarcodeScanner(): BarcodeScanner = remember { BarcodeScanner() }

/** Anahtar pencerenin en-ust (en-son sunulmus) view controller'ini bulur — barkod tarayicisi
 * bunun uzerinden modal sunulur. `keyWindow` API'si iOS 13+'ta deprecated (multi-scene) ama
 * proje `deploymentTarget: iOS 14.0` + tek-scene basit bir uygulama oldugundan (bkz.
 * iosApp/project.yml) calisir; multi-scene destegi bu Faz'in KAPSAMI DISINDA. */
@OptIn(ExperimentalForeignApi::class)
private fun topViewController(): UIViewController? {
    var top = UIApplication.sharedApplication.keyWindow?.rootViewController ?: return null
    while (true) {
        top = top!!.presentedViewController ?: break
    }
    return top
}

/** [AVCaptureMetadataOutputObjectsDelegateProtocol] implementasyonu — metadata cikisinda ilk
 * gecerli `stringValue`'yu [onCode] ile geri bildirir. Ana kuyrukta cagirilmasi
 * `output.setMetadataObjectsDelegate(delegate, queue = dispatch_get_main_queue())` ile
 * [BarcodeScannerViewController.configureSession] tarafinda garanti edilir. */
@OptIn(ExperimentalForeignApi::class)
private class BarcodeCaptureDelegate(
    private val onCode: (String) -> Unit,
) : NSObject(), AVCaptureMetadataOutputObjectsDelegateProtocol {

    override fun captureOutput(
        output: AVCaptureOutput,
        didOutputMetadataObjects: List<*>,
        fromConnection: AVCaptureConnection,
    ) {
        val code = didOutputMetadataObjects
            .filterIsInstance<AVMetadataMachineReadableCodeObject>()
            .firstOrNull()
            ?.stringValue
            ?: return
        onCode(code)
    }
}

/**
 * Tam-ekran kamera tarayici — AVFoundation ile kendi UI'ini kurar (Auto Layout kullanilmadan,
 * bilerek sadece `frame`/`CGRectMake` ile — Kotlin/Native + NSLayoutConstraint anchor zincirleri
 * bu interop yuzeyinde gereksiz risk eklerdi). Overlay: ortalanmis kare hedef cerceve + kisa
 * yonlendirme metni + sag-ust "Kapat" butonu.
 *
 * Session yasam dongusu:
 * - `viewWillAppear`'da arka-plan kuyrugunda ([sessionQueue]) `startRunning()`.
 * - `viewWillDisappear`'da ayni kuyrukta `stopRunning()` (Apple: start/stop UI thread'i
 *   bloklayabilir, bu yuzden asla ana kuyrukta cagrilmaz).
 * - Bir sonuc bulunduysa veya kullanici iptal ettiyse [finishWithResult] tek seferlik calisir
 *   ([finished] guard'i) — oturumu durdurur, VC'yi kapatir, [onResult]'i ana kuyrukta cagirir.
 */
@OptIn(ExperimentalForeignApi::class)
private class BarcodeScannerViewController(
    private val onResult: (String?) -> Unit,
) : UIViewController(nibName = null, bundle = null) {

    private val session = AVCaptureSession()
    private val sessionQueue = dispatch_queue_create("com.calibrahub.mobile.barcode.session", null)
    private val captureDelegate = BarcodeCaptureDelegate { code -> finishWithResult(code) }

    private var previewLayer: AVCaptureVideoPreviewLayer? = null
    private var frameGuideView: UIView? = null
    private var hintLabel: UILabel? = null
    private var closeButton: UIButton? = null
    private var finished = false

    override fun viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = UIColor.blackColor
        configureSession()
        setupOverlay()
    }

    override fun viewWillAppear(animated: Boolean) {
        super.viewWillAppear(animated)
        dispatch_async(sessionQueue) {
            if (!session.running) session.startRunning()
        }
    }

    override fun viewWillDisappear(animated: Boolean) {
        super.viewWillDisappear(animated)
        dispatch_async(sessionQueue) {
            if (session.running) session.stopRunning()
        }
    }

    override fun viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        val bounds = view.bounds
        val width = bounds.useContents { size.width }
        val height = bounds.useContents { size.height }

        previewLayer?.frame = bounds

        val guideSize = 240.0
        val guideFrame: CValue<platform.CoreGraphics.CGRect> = CGRectMake(
            x = (width - guideSize) / 2.0,
            y = (height - guideSize) / 2.0,
            width = guideSize,
            height = guideSize,
        )
        frameGuideView?.let { it.setFrame(guideFrame) }
        val guideBottom = (height - guideSize) / 2.0 + guideSize

        hintLabel?.let {
            it.setFrame(
                CGRectMake(
                    x = 16.0,
                    y = guideBottom + 16.0,
                    width = width - 32.0,
                    height = 24.0,
                )
            )
        }

        val topInset = view.safeAreaInsets.useContents { top }
        closeButton?.let {
            it.setFrame(
                CGRectMake(
                    x = width - 96.0,
                    y = topInset + 12.0,
                    width = 80.0,
                    height = 40.0,
                )
            )
        }
    }

    private fun configureSession() {
        val device = AVCaptureDevice.defaultDeviceWithMediaType(AVMediaTypeVideo)
        if (device == null) {
            finishWithResult(null)
            return
        }

        memScoped {
            val errorVar = alloc<ObjCObjectVar<NSError?>>()
            val input = AVCaptureDeviceInput.deviceInputWithDevice(device, errorVar.ptr)
            if (input == null || errorVar.value != null) {
                finishWithResult(null)
                return
            }
            if (session.canAddInput(input)) {
                session.addInput(input)
            } else {
                finishWithResult(null)
                return
            }
        }

        val output = AVCaptureMetadataOutput()
        if (session.canAddOutput(output)) {
            session.addOutput(output)
        } else {
            finishWithResult(null)
            return
        }
        output.setMetadataObjectsDelegate(captureDelegate, dispatch_get_main_queue())
        output.metadataObjectTypes = listOf(
            AVMetadataObjectTypeQRCode,
            AVMetadataObjectTypeEAN13Code,
            AVMetadataObjectTypeEAN8Code,
            AVMetadataObjectTypeCode128Code,
            AVMetadataObjectTypeCode39Code,
            AVMetadataObjectTypeCode93Code,
            AVMetadataObjectTypeUPCECode,
            AVMetadataObjectTypeITF14Code,
            AVMetadataObjectTypeDataMatrixCode,
        )

        val layer = AVCaptureVideoPreviewLayer(session = session)
        layer.videoGravity = AVLayerVideoGravityResizeAspectFill
        layer.frame = view.bounds
        view.layer.addSublayer(layer)
        previewLayer = layer
    }

    private fun setupOverlay() {
        val guide = UIView()
        guide.layer.borderColor = UIColor.whiteColor.CGColor
        guide.layer.borderWidth = 2.0
        guide.layer.cornerRadius = 12.0
        guide.userInteractionEnabled = false
        view.addSubview(guide)
        frameGuideView = guide

        val hint = UILabel()
        hint.text = "Barkodu kare içine hizalayın"
        hint.textColor = UIColor.whiteColor
        hint.textAlignment = NSTextAlignmentCenter
        hint.font = UIFont.systemFontOfSize(14.0)
        view.addSubview(hint)
        hintLabel = hint

        val close = UIButton()
        close.setTitle("Kapat", forState = UIControlStateNormal)
        close.setTitleColor(UIColor.whiteColor, forState = UIControlStateNormal)
        close.backgroundColor = UIColor.colorWithWhite(0.0, alpha = 0.4)
        close.layer.cornerRadius = 8.0
        close.addTarget(
            target = this,
            action = NSSelectorFromString("onCloseTapped:"),
            forControlEvents = UIControlEventTouchUpInside,
        )
        view.addSubview(close)
        closeButton = close
    }

    /** UIButton target-action koprusu — Kotlin/Native'de bir Objective-C selector ile
     * cagirilabilmesi icin [ObjCAction] ile isaretlenir; sinif [NSObject] soyundan geldigi
     * icin (UIViewController -> UIResponder -> NSObject) Objective-C runtime'inda gorunurdur.
     * Imza, standart UIKit target-action konvansiyonuna uyar (`sender` parametresi). */
    @ObjCAction
    fun onCloseTapped(sender: UIButton?) {
        finishWithResult(null)
    }

    private fun finishWithResult(code: String?) {
        if (finished) return
        finished = true
        dispatch_async(sessionQueue) {
            if (session.running) session.stopRunning()
        }
        dispatch_async(dispatch_get_main_queue()) {
            dismissViewControllerAnimated(true, completion = null)
            onResult(code)
        }
    }
}
