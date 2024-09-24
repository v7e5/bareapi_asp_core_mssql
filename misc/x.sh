#!/usr/bin/zsh
set -euo pipefail

cke='./misc/cookie'

bulk() {
  while IFS= read -r l; do
    curl -vs -X POST \
      --cookie ${cke} \
      --cookie-jar ${cke} \
      -H 'content-type: application/json' \
      -H 'accept: application/json' \
      --data-binary "${l}" \
      'http://0.0.0.0:8000/todo/create' || :
  done <./misc/todo_2.txt
}

ccc() {
  local _i=0
  [[ ! -f ${cke} ]] && touch ${cke}

  local a=(
    user/profile
    login
    'echo'
    todo/list
    q
    now
    todo/create
    todo/delete
    todo/update
    category/list
    category/create
    'logout'
    category/delete
    category/update
    hailstone
    env
    user/resetpass
    user/list
    user/create
    user/delete
  )

  #-o /dev/null \
  #--write-out "@./write_out_fmt.yml" \
  #--data-binary "$(./misc/q.sh -x)" \
  curl -vs -X POST \
    --cookie ${cke} \
    --cookie-jar ${cke} \
    -H 'content-type: application/json' \
    -H 'accept: application/json' \
    --data-binary "$(./misc/q.sh -x)" \
    'http://0.0.0.0:8000/'${a[1]} | jq
}

w() {
  local a=(
    x.sh
    q.sh
    q.sql
  )

  inotifywait -mr -e close_write -e delete -e moved_to ./ \
    | while read d e f; do
        if [[ $a[(Ie)${f}] -ne 0 ]]; then
          clear -x
          cl -b 27  -f 51 -o '------------------------------------------------'
          echo
          ./misc/x.sh -c || :
        fi
      done
}

_k=(${(ok)functions:#_*})
_v=(${(oM)_k#[a-z]*})
typeset -A _o
_o=(${_v:^_k})

eval 'zparseopts -D -E -F -a _a '${_v}

[[ ${#_a} -eq 0  ]] && \
  paste -d ' ' <(print -l '\-'${(j:\n-:)_v}) <(print -l ${_k}) && exit

_a=('$_o['${^_a#-}']')
eval ${(F)_a}
exit
