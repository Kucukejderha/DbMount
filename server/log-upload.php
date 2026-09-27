<?php
// ASCOS DbMount hata kaydi toplama noktasi
// Bu dosyayi web sunucusuna (orn. rotaniz.com/ascos-araclar/dbmount/log-upload.php)
// kopyalayin. Ayni dizinde 'logs' alt klasoru olusturulur; yazilabilir olmalidir.
header('Content-Type: text/plain; charset=utf-8');

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    http_response_code(405);
    echo 'Yalnizca POST kabul edilir';
    exit;
}

$body = file_get_contents('php://input');
if ($body === false || strlen($body) > 5 * 1024 * 1024) {
    http_response_code(413);
    echo 'Kayit cok buyuk veya okunamadi';
    exit;
}

$dir = __DIR__ . '/logs';
if (!is_dir($dir)) {
    @mkdir($dir, 0775, true);
}
if (!is_dir($dir) || !is_writable($dir)) {
    http_response_code(500);
    echo 'Sunucu tarafinda logs dizini yazilabilir degil';
    exit;
}

$product = isset($_GET['product']) ? preg_replace('/[^A-Za-z0-9 ._-]/', '', $_GET['product']) : 'DbMount';
$version = isset($_GET['version']) ? preg_replace('/[^0-9.]/', '', $_GET['version']) : '0';
$machine = isset($_GET['machine']) ? preg_replace('/[^A-Za-z0-9 ._-]/', '', $_GET['machine']) : 'bilinmiyor';
$date = gmdate('Y-m-d');
$file = sprintf('%s/%s-%s-%s.log', $dir, $product, $date, str_replace('.', '_', $version));
$header = sprintf(
    "---- %s ---- urun=%s %s makine=%s ip=%s\n",
    gmdate('Y-m-d H:i:s'), $product, $version, $machine,
    $_SERVER['REMOTE_ADDR'] ?? ''
);

if (@file_put_contents($file, $header . $body . "\n\n", FILE_APPEND | LOCK_EX) === false) {
    http_response_code(500);
    echo 'Kayit yazilamadi';
    exit;
}

echo 'OK';
