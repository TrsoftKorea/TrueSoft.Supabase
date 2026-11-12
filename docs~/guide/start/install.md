# 설치

TrueBase를 Unity 프로젝트에 설치하고, 첫 번째 기능을 동작시키기까지의 과정을 안내합니다.

## 준비물

- Unity 2022.3 이상. 2022.3.62f2와 6000.3.9f1에서 컴파일을 확인했습니다
- PC에 설치된 Git. Package Manager가 Git 주소로 패키지를 받을 때 사용합니다

## 패키지 추가

**Window > Package Manager > + > Add package from git URL**

```
https://github.com/trsoftkorea/TrueSoft.Supabase.git
```

SDK가 사용하는 Newtonsoft.Json 패키지는 함께 설치됩니다.

## 판 고르기 {#version}

주소 끝에 `#v` 와 판 번호를 붙이면 그 판을 받습니다. 붙이지 않으면 가장 최신 판을 받습니다.

```
https://github.com/trsoftkorea/TrueSoft.Supabase.git#v0.1.1
```

이미 설치한 게임은 `Packages/manifest.json` 의 `com.truesoft.supabase` 주소 끝 판 번호를 바꾸면 그 판으로 옮겨 갑니다. 판 번호 없이 설치했으면 `Packages/packages-lock.json` 이 받은 시점의 판에 묶여 있어 최신 판이 나와도 저절로 바뀌지 않습니다.
