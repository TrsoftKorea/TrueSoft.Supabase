# 발급 절차

Google Play 영수증 검증에 필요한 서비스 계정 JSON 키 발급 절차입니다.

---

**1.** Google Cloud 콘솔에 접속해 JSON을 발급받을 프로젝트를 선택합니다. 프로젝트가 없으면 새로 생성합니다.

![](/google-service-account/image18.png)

---

**2.** 빠른 액세스 또는 좌측 탭에서 **API 및 서비스**를 선택합니다.

![](/google-service-account/image14.png)

---

**3.** 상단의 **+ API 및 서비스 사용 설정**을 선택합니다.

![](/google-service-account/image11.png)

---

**4.** 아래 두 API를 검색해 각각 사용 설정합니다.

- **Google Play Android Developer API**
- **Google Play Games Services Publishing API**

![](/google-service-account/image7.png)

---

**5.** **API 및 서비스**로 이동 후, 좌측 메뉴에서 **사용자 인증 정보** (열쇠 모양)를 선택합니다.

![](/google-service-account/image9.png)

---

**6.** 상단의 **+ 사용자 인증 정보 만들기**를 선택 후 **서비스 계정**을 추가합니다.

![](/google-service-account/image15.png)

---

**7.** 이름·ID·설명을 입력하고 **만들고 계속하기**를 선택합니다. 이름은 자유롭게 구성합니다.

![](/google-service-account/image12.png)

---

**8.** 역할 선택 후 **소유자** 역할을 추가하고 **계속**을 선택합니다.

![](/google-service-account/image5.png)

---

**9.** **서비스 계정 사용자 역할** 및 **서비스 계정 관리자 역할** 항목에 현재 접속한 계정의 이메일 주소를 입력하고 **완료**를 선택합니다.

![](/google-service-account/image3.png)

---

**10.** **API 및 서비스 > 서비스 계정**에서 방금 생성한 계정을 선택합니다.

![](/google-service-account/image2.png)

---

**11.** 상단 탭에서 **키** 메뉴로 이동합니다.

![](/google-service-account/image6.png)

---

**12.** **키 추가 > 새 키 만들기**를 선택합니다.

![](/google-service-account/image16.png)

---

**13.** **JSON**을 선택하고 **만들기**를 클릭합니다. 저장된 JSON 파일을 확인하세요.

::: warning
생성된 JSON 파일은 이후 재다운로드가 불가능합니다. 안전한 곳에 보관하세요.
:::

![](/google-service-account/image8.png)
