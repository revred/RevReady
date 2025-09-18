#include "../abi/revready.h"
#include <string.h>
#include <stdlib.h>
static __thread const char* g_last_error = "";
static void set_err(const char* s){ g_last_error = s; }
const char* rev_last_error(void){ return g_last_error; }
static void* xalloc(size_t n){ void* p = malloc(n); if(!p){ set_err("OOM"); } return p; }
void rev_free(void* p){ if(p) free(p); }
static int raw_copy(const unsigned char* in, unsigned in_len, unsigned char* out, unsigned out_cap, unsigned* out_len){
    if(!out) return -2; if(out_cap < in_len) return -1; memcpy(out, in, in_len); *out_len = in_len; return 0; }
static rev_result_t do_batch(rev_batch_t* batch){
    if(!batch || !batch->descs){ set_err("bad args"); return (rev_result_t){22,0}; }
    unsigned total=0;
    for(uint32_t i=0;i<batch->count;i++){
        rev_desc_t* d=&batch->descs[i];
        if(!d->in.ptr || d->in.len==0){ set_err("empty input"); return (rev_result_t){22,0}; }
        unsigned out_len=0;
        if(d->out.ptr && d->out.len>=d->in.len){
            int rc = raw_copy((const unsigned char*)d->in.ptr, d->in.len, (unsigned char*)d->out.ptr, d->out.len, &out_len);
            if(rc!=0){ set_err("copy failed"); return (rev_result_t){5,0}; }
        } else if(d->flags & 1){
            void* buf = xalloc(d->in.len); if(!buf) return (rev_result_t){12,0};
            memcpy(buf, d->in.ptr, d->in.len); d->out.ptr = buf; d->out.len = d->in.len; out_len = d->in.len;
        } else { set_err("no output buffer and realloc not allowed"); return (rev_result_t){22,0}; }
        total += out_len;
    } return (rev_result_t){0,total};
}
rev_result_t rev_reduce_batch(rev_batch_t* batch){ return do_batch(batch); }
rev_result_t rev_inflate_batch(rev_batch_t* batch){ return do_batch(batch); }
